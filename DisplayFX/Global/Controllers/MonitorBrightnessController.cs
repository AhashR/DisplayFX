using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NLog;
using WindowsDisplayAPI;

namespace DisplayFX.Global.Controllers;

/// <summary>
///     Controls the physical backlight brightness of monitors over DDC/CI,
///     using the Windows Monitor Configuration API (dxva2.dll) - the same
///     mechanism used by Twinkle Tray and Monitorian.
/// </summary>
public class MonitorBrightnessController
{
    /// <summary>VCP code for image luminance (backlight brightness).</summary>
    private const byte BrightnessVcpCode = 0x10;

    /// <summary>VCP code for the display power mode.</summary>
    private const byte DisplayPowerModeVcpCode = 0xD6;

    /// <summary>MC_CAPS_BRIGHTNESS: the monitor reports DDC/CI brightness support.</summary>
    private const uint MonitorCapabilitiesBrightness = 0x00000002;

    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, BrightnessStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _lastKnown = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _displaysAsleep;
    public bool IsDisplaySleeping => _displaysAsleep;
    public event Action<bool>? DisplayPowerChanged;
    public int? GetLastKnownBrightness(Display display) => _lastKnown.TryGetValue(display.DevicePath, out var value) ? value : null;
    public void NotifyDisplayPower(bool awake)
    {
        _displaysAsleep = !awake;
        if (awake) InvalidateTransports();
        DisplayPowerChanged?.Invoke(awake);
    }
    public void InvalidateTransports()
    {
        foreach (var channel in _channels.Values) channel.Invalidated = true;
    }

    private sealed class Channel
    {
        public bool? HighLevel;
        public uint Minimum;
        public uint Maximum = 100;
        public volatile bool Invalidated;
    }
    private static void RefreshChannel(Channel channel)
    {
        if (!channel.Invalidated) return;
        channel.HighLevel = null;
        channel.Minimum = 0;
        channel.Maximum = 100;
        channel.Invalidated = false;
    }
    private readonly ConcurrentDictionary<string, Channel> _channels = new(StringComparer.OrdinalIgnoreCase);

    public event Action<Display, BrightnessStatus>? StatusChanged;

    public BrightnessStatus? GetStatus(Display display) =>
        _statuses.TryGetValue(display.DevicePath, out var status) ? status : null;

    protected void RecordStatus(Display display, BrightnessStatus status)
    {
        _statuses[display.DevicePath] = status;
        if (!status.IsError && status.Brightness.HasValue) _lastKnown[display.DevicePath] = status.Brightness.Value;
        // UI feedback must never turn a successful hardware operation into a failure.
        if (StatusChanged == null) return;
        foreach (Action<Display, BrightnessStatus> subscriber in StatusChanged.GetInvocationList())
        {
            try { subscriber(display, status); }
            catch (Exception exception) { _logger.Warn(exception, "Could not update brightness status."); }
        }
    }

    public MonitorBrightnessController(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Reads the current backlight brightness (0-100) of the given display,
    ///     normalized against the monitor's reported VCP maximum. Returns null
    ///     when the monitor does not expose DDC/CI brightness control.
    /// </summary>
    public virtual int? GetBrightness(Display display)
    {
        if (display is null || IsDisplaySleeping) return null;
        var channel = _channels.GetOrAdd(display.DevicePath, _ => new Channel());
        lock (channel)
        {
            RefreshChannel(channel);
            var brightness = TryGetBrightness(display, channel);
            if (!brightness.HasValue) channel.Invalidated = true;
            RecordStatus(display, brightness.HasValue
                ? new BrightnessStatus("", false, brightness)
                : new BrightnessStatus("Brightness unavailable. Check the monitor connection and DDC/CI setting.", true));
            return brightness;
        }
    }

    private int? TryGetBrightness(Display display, Channel channel)
    {
        if (display is null)
            return null;

        try
        {
            var monitorHandle = GetMonitorHandle(display);
            if (monitorHandle == IntPtr.Zero)
                return null;

            var physicalMonitors = GetPhysicalMonitors(monitorHandle);
            if (physicalMonitors is null || physicalMonitors.Length == 0)
                return null;

            try
            {
                foreach (var physicalMonitor in physicalMonitors)
                {
                    if (ReadBrightness(physicalMonitor.Handle, channel, out var value))
                        return value;
                }
            }
            finally
            {
                ReleasePhysicalMonitors(physicalMonitors);
            }
        }
        catch (Exception e)
        {
            _logger.Warn(e, "Failed to read hardware brightness for {0}.", display.DevicePath);
        }

        return null;
    }

    /// <summary>
    ///     Determines whether the display can have its backlight brightness
    ///     controlled over DDC/CI, based on the monitor's reported capabilities.
    ///     When the capabilities can't be determined the result defaults to true so
    ///     displays that accept writes but fail reads remain adjustable.
    /// </summary>
    public virtual bool SupportsBrightness(Display display)
    {
        if (display is null || IsDisplaySleeping) return false;
        var channel = _channels.GetOrAdd(display.DevicePath, _ => new Channel());
        lock (channel) return channel.HighLevel.HasValue || TrySupportsBrightness(display);
    }

    private bool TrySupportsBrightness(Display display)
    {
        if (display is null)
            return false;

        try
        {
            var monitorHandle = GetMonitorHandle(display);
            if (monitorHandle == IntPtr.Zero)
                return false;

            var physicalMonitors = GetPhysicalMonitors(monitorHandle);
            if (physicalMonitors is null || physicalMonitors.Length == 0)
                return false;

            try
            {
                var hasUnknownCapabilities = false;
                foreach (var physicalMonitor in physicalMonitors)
                {
                    if (GetMonitorCapabilities(physicalMonitor.Handle, out var capabilities, out _))
                    {
                        if ((capabilities & MonitorCapabilitiesBrightness) != 0)
                            return true;
                    }
                    else
                    {
                        hasUnknownCapabilities = true;
                    }
                }

                return hasUnknownCapabilities;
            }
            finally
            {
                ReleasePhysicalMonitors(physicalMonitors);
            }
        }
        catch (Exception e)
        {
            _logger.Warn(e, "Failed to query brightness support for {0}.", display.DevicePath);
        }

        return true;
    }

    /// <summary>
    ///     Sets the hardware brightness (0-100) of the given display, scaling the
    ///     value against the monitor's reported VCP maximum. Failures (e.g.
    ///     monitors that don't support DDC/CI) are logged and ignored so they
    ///     never block the rest of a profile from being applied.
    /// </summary>
    /// <returns>True when a physical monitor accepted the brightness write.</returns>
    public virtual bool SetBrightness(Display display, int brightness)
    {
        if (display is null || IsDisplaySleeping) return false;
        var channel = _channels.GetOrAdd(display.DevicePath, _ => new Channel());
        lock (channel)
        {
            RefreshChannel(channel);
            var applied = TrySetBrightness(display, brightness, channel);
            if (!applied) channel.Invalidated = true;
            RecordStatus(display, new BrightnessStatus(applied ? "" :
                "Brightness change failed. Check the monitor connection and DDC/CI setting.", !applied,
                Math.Clamp(brightness, 0, 100)));
            return applied;
        }
    }

    private bool TrySetBrightness(Display display, int brightness, Channel channel)
    {
        if (display is null)
        {
            _logger.Warn("Cannot set hardware brightness: display has no screen.");
            return false;
        }

        try
        {
            var monitorHandle = GetMonitorHandle(display);
            if (monitorHandle == IntPtr.Zero)
            {
                _logger.Warn("Could not resolve a monitor handle for {0}.", display.DevicePath);
                return false;
            }

            var physicalMonitors = GetPhysicalMonitors(monitorHandle);
            if (physicalMonitors is null || physicalMonitors.Length == 0)
            {
                _logger.Warn("Monitor {0} does not support DDC/CI brightness.", display.DevicePath);
                return false;
            }

            try
            {
                var targetBrightness = Math.Clamp(brightness, 0, 100);

                foreach (var physicalMonitor in physicalMonitors)
                {
                    // Use the same API and range that worked for reading this monitor.
                    // Re-query only on first contact or when the preferred write fails.
                    if (!channel.HighLevel.HasValue) ReadBrightness(physicalMonitor.Handle, channel, out _);
                    var highLevel = channel.HighLevel ?? true;
                    var value = ScaleBrightness(targetBrightness, channel.Minimum, channel.Maximum);
                    if (WriteBrightness(physicalMonitor.Handle, highLevel, value)) return true;

                    var fallback = new Channel();
                    // A few monitors accept writes through an API that cannot read their level.
                    // If that read is unavailable, its default 0–100 range is still worth trying.
                    ReadBrightness(physicalMonitor.Handle, fallback, out _, !highLevel);
                    if (WriteBrightness(physicalMonitor.Handle, !highLevel,
                            ScaleBrightness(targetBrightness, fallback.Minimum, fallback.Maximum)))
                    {
                        channel.HighLevel = !highLevel;
                        channel.Minimum = fallback.Minimum;
                        channel.Maximum = fallback.Maximum;
                        return true;
                    }
                }

                _logger.Warn("Failed to set hardware brightness on {0}.", display.DevicePath);
            }
            finally
            {
                ReleasePhysicalMonitors(physicalMonitors);
            }
        }
        catch (Exception e)
        {
            _logger.Warn(e, "Failed to set hardware brightness for {0}.", display.DevicePath);
        }

        return false;
    }

    /// <summary>
    ///     Turns off the given displays using the DDC/CI display power mode VCP
    ///     feature. Monitors wake again on mouse or keyboard input.
    /// </summary>
    public void TurnOffDisplays(IEnumerable<Display> displays)
    {
        foreach (var display in displays)
        {
            var channel = _channels.GetOrAdd(display.DevicePath, _ => new Channel());
            lock (channel) SetPowerState(display, 5);
        }
    }

    private void SetPowerState(Display display, uint powerState)
    {
        if (display is null)
            return;

        try
        {
            var monitorHandle = GetMonitorHandle(display);
            if (monitorHandle == IntPtr.Zero)
                return;

            var physicalMonitors = GetPhysicalMonitors(monitorHandle);
            if (physicalMonitors is null || physicalMonitors.Length == 0)
                return;

            try
            {
                foreach (var physicalMonitor in physicalMonitors)
                    SetVCPFeature(physicalMonitor.Handle, DisplayPowerModeVcpCode, powerState);
            }
            finally
            {
                ReleasePhysicalMonitors(physicalMonitors);
            }
        }
        catch (Exception e)
        {
            _logger.Warn(e, "Failed to change power state for {0}.", display.DevicePath);
        }
    }

    private bool ReadBrightness(IntPtr handle, Channel channel, out int brightness, bool? onlyHighLevel = null)
    {
        var preferred = onlyHighLevel ?? channel.HighLevel ?? true;
        foreach (var highLevel in onlyHighLevel.HasValue ? new[] { preferred } : new[] { preferred, !preferred })
        {
            uint min = 0, current, max;
            var read = highLevel ? ReadHighLevel(handle, out min, out current, out max) : ReadVcp(handle, out current, out max);
            if (!read) continue;
            channel.HighLevel = highLevel;
            channel.Minimum = min;
            channel.Maximum = max == 0 ? 100 : max;
            brightness = highLevel ? NormalizeBrightness(min, current, max) : NormalizeBrightness(current, max);
            return true;
        }
        brightness = 0;
        return false;
    }

    private bool WriteBrightness(IntPtr handle, bool highLevel, uint value) =>
        highLevel ? WriteHighLevel(handle, value) : WriteVcp(handle, value);

    protected virtual bool ReadHighLevel(IntPtr handle, out uint min, out uint current, out uint max) =>
        GetMonitorBrightness(handle, out min, out current, out max);
    protected virtual bool ReadVcp(IntPtr handle, out uint current, out uint max) =>
        GetVCPFeatureAndVCPFeatureReply(handle, BrightnessVcpCode, out _, out current, out max);
    protected virtual bool WriteHighLevel(IntPtr handle, uint value) => SetMonitorBrightness(handle, value);
    protected virtual bool WriteVcp(IntPtr handle, uint value) => SetVCPFeature(handle, BrightnessVcpCode, value);
    protected virtual void ReleasePhysicalMonitors(PhysicalMonitor[] monitors) =>
        DestroyPhysicalMonitors((uint)monitors.Length, monitors);

    private static int NormalizeBrightness(uint current, uint max)
    {
        // Some monitors report a maximum of 0; the DDC/CI convention is 0-100.
        if (max == 0)
            max = 100;

        return (int)Math.Clamp(Math.Round(current * 100.0 / max), 0, 100);
    }

    private static int NormalizeBrightness(uint min, uint current, uint max)
    {
        if (max <= min)
            return (int)Math.Clamp(current, 0u, 100u);

        return (int)Math.Clamp(Math.Round(((double)current - min) * 100.0 / (max - min)), 0, 100);
    }

    private static uint ScaleBrightness(int brightness, uint max)
    {
        if (max == 0)
            return (uint)Math.Clamp(brightness, 0, 100);

        return ScaleBrightness(brightness, 0, max);
    }

    private static uint ScaleBrightness(int brightness, uint min, uint max)
    {
        if (max <= min)
            return (uint)Math.Clamp(brightness, 0, 100);

        return (uint)Math.Clamp(Math.Round(min + Math.Clamp(brightness, 0, 100) * (max - min) / 100.0), min, max);
    }

    protected virtual IntPtr GetMonitorHandle(Display display)
    {
        if (!display.IsAvailable)
            return IntPtr.Zero;

        var screen = display.DisplayScreen;
        if (screen is null)
            return IntPtr.Zero;

        var bounds = screen.Bounds;
        var center = new Point
        {
            X = bounds.X + bounds.Width / 2,
            Y = bounds.Y + bounds.Height / 2
        };

        return MonitorFromPoint(center, MonitorDefaultToNull);
    }

    protected virtual PhysicalMonitor[]? GetPhysicalMonitors(IntPtr monitorHandle)
    {
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitorHandle, out var count) || count == 0)
            return null;

        var physicalMonitors = new PhysicalMonitor[count];
        return GetPhysicalMonitorsFromHMONITOR(monitorHandle, count, physicalMonitors)
            ? physicalMonitors
            : null;
    }

    private const uint MonitorDefaultToNull = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    protected struct PhysicalMonitor
    {
        public IntPtr Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("dxva2.dll", EntryPoint = "GetNumberOfPhysicalMonitorsFromHMONITOR")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(
        IntPtr monitorHandle, out uint count);

    [DllImport("dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr monitorHandle, uint physicalMonitorArraySize,
        [Out] PhysicalMonitor[] physicalMonitorArray);

    [DllImport("dxva2.dll", EntryPoint = "DestroyPhysicalMonitors")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitors(
        uint physicalMonitorArraySize, [In] PhysicalMonitor[] physicalMonitorArray);

    [DllImport("dxva2.dll", EntryPoint = "GetVCPFeatureAndVCPFeatureReply")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(
        IntPtr monitorHandle, byte vcpCode, out int vcpCodeType,
        out uint currentValue, out uint maximumValue);

    [DllImport("dxva2.dll", EntryPoint = "GetMonitorCapabilities")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorCapabilities(
        IntPtr monitorHandle, out uint monitorCapabilities, out uint supportedColorTemperatures);

    [DllImport("dxva2.dll", EntryPoint = "GetMonitorBrightness")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorBrightness(
        IntPtr monitorHandle, out uint minimumBrightness, out uint currentBrightness, out uint maximumBrightness);

    [DllImport("dxva2.dll", EntryPoint = "SetMonitorBrightness")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMonitorBrightness(IntPtr monitorHandle, uint newBrightness);

    [DllImport("dxva2.dll", EntryPoint = "SetVCPFeature")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetVCPFeature(IntPtr monitorHandle, byte vcpCode, uint newValue);
}
