using System;
using System.Collections.Generic;
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
    private const byte DisplayPowerModeVcpCode = 0xD6;

    private readonly ILogger _logger;

    public MonitorBrightnessController(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Reads the current backlight brightness (0-100) of the given display,
    ///     normalized against the monitor's supported VCP range. Returns null when
    ///     the monitor does not support DDC/CI brightness control.
    /// </summary>
    public int? GetBrightness(Display display)
    {
        if (display?.DisplayScreen == null)
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
                    if (GetMonitorBrightness(physicalMonitor.Handle, out var min, out var current, out var max))
                        return NormalizeBrightness(min, current, max);
                }
            }
            finally
            {
                DestroyPhysicalMonitors((uint)physicalMonitors.Length, physicalMonitors);
            }
        }
        catch (Exception e)
        {
            _logger.Warn(e, "Failed to read hardware brightness for {0}.", display.DisplayName);
        }

        return null;
    }

    /// <summary>
    ///     Sets the hardware brightness (0-100) of the given display, scaling the
    ///     value against the monitor's supported VCP range. Failures (e.g. monitors
    ///     that don't support DDC/CI) are logged and ignored so they never block
    ///     the rest of a profile from being applied.
    /// </summary>
    public void SetBrightness(Display display, int brightness)
    {
        if (display?.DisplayScreen == null)
        {
            _logger.Warn("Cannot set hardware brightness: display has no screen.");
            return;
        }

        try
        {
            var monitorHandle = GetMonitorHandle(display);
            if (monitorHandle == IntPtr.Zero)
            {
                _logger.Warn("Could not resolve a monitor handle for {0}.", display.DisplayName);
                return;
            }

            var physicalMonitors = GetPhysicalMonitors(monitorHandle);
            if (physicalMonitors is null || physicalMonitors.Length == 0)
            {
                _logger.Warn("Monitor {0} does not support DDC/CI brightness.", display.DisplayName);
                return;
            }

            try
            {
                var targetBrightness = Math.Clamp(brightness, 0, 100);

                foreach (var physicalMonitor in physicalMonitors)
                {
                    // Try to resolve the monitor's real VCP range so the 0-100 value
                    // maps correctly. Fall back to the raw value when unsupported.
                    var rawBrightness = targetBrightness;
                    if (GetMonitorBrightness(physicalMonitor.Handle, out var min, out _, out var max))
                        rawBrightness = ScaleBrightness(targetBrightness, min, max);

                    if (SetMonitorBrightness(physicalMonitor.Handle, (uint)rawBrightness))
                        return;
                }

                _logger.Warn("Failed to set hardware brightness on {0}.", display.DisplayName);
            }
            finally
            {
                DestroyPhysicalMonitors((uint)physicalMonitors.Length, physicalMonitors);
            }
        }
        catch (Exception e)
        {
            _logger.Warn(e, "Failed to set hardware brightness for {0}.", display.DisplayName);
        }
    }

    /// <summary>
    ///     Turns off the given displays using the DDC/CI display power mode VCP
    ///     feature. Monitors wake again on mouse or keyboard input.
    /// </summary>
    public void TurnOffDisplays(IEnumerable<Display> displays)
    {
        foreach (var display in displays)
            SetPowerState(display, 5); // 5 = power off
    }

    private void SetPowerState(Display display, uint powerState)
    {
        if (display?.DisplayScreen == null)
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
                DestroyPhysicalMonitors((uint)physicalMonitors.Length, physicalMonitors);
            }
        }
        catch (Exception e)
        {
            _logger.Warn(e, "Failed to change power state for {0}.", display.DisplayName);
        }
    }

    private static int NormalizeBrightness(uint min, uint current, uint max)
    {
        if (max <= min)
            return (int)Math.Clamp(current, 0u, 100u);

        return (int)Math.Round((current - min) * 100.0 / (max - min));
    }

    private static int ScaleBrightness(int brightness, uint min, uint max)
    {
        if (max <= min)
            return Math.Clamp(brightness, 0, 100);

        var scaled = min + (max - min) * brightness / 100.0;
        return (int)Math.Clamp(Math.Round(scaled), min, max);
    }

    private static IntPtr GetMonitorHandle(Display display)
    {
        var bounds = display.DisplayScreen.Bounds;
        var center = new Point
        {
            X = bounds.X + bounds.Width / 2,
            Y = bounds.Y + bounds.Height / 2
        };

        return MonitorFromPoint(center, MonitorDefaultToNearest);
    }

    private static PhysicalMonitor[]? GetPhysicalMonitors(IntPtr monitorHandle)
    {
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitorHandle, out var count) || count == 0)
            return null;

        var physicalMonitors = new PhysicalMonitor[count];
        return GetPhysicalMonitorsFromHMONITOR(monitorHandle, count, physicalMonitors)
            ? physicalMonitors
            : null;
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PhysicalMonitor
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
        uint physicalMonitorArraySize, [Out] PhysicalMonitor[] physicalMonitorArray);

    [DllImport("dxva2.dll", EntryPoint = "SetMonitorBrightness")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMonitorBrightness(IntPtr monitorHandle, uint newBrightness);

    [DllImport("dxva2.dll", EntryPoint = "GetMonitorBrightness")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorBrightness(
        IntPtr monitorHandle, out uint minimumBrightness, out uint currentBrightness, out uint maximumBrightness);

    [DllImport("dxva2.dll", EntryPoint = "SetVCPFeature")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetVCPFeature(IntPtr monitorHandle, byte vcpCode, uint newValue);
}
