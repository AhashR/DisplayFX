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
    /// <summary>VCP code for image luminance (backlight brightness).</summary>
    private const byte BrightnessVcpCode = 0x10;

    /// <summary>VCP code for the display power mode.</summary>
    private const byte DisplayPowerModeVcpCode = 0xD6;

    private readonly ILogger _logger;

    public MonitorBrightnessController(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Reads the current backlight brightness (0-100) of the given display,
    ///     normalized against the monitor's reported VCP maximum. Returns null
    ///     when the monitor does not expose DDC/CI brightness control.
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
                    if (GetVCPFeatureAndVCPFeatureReply(physicalMonitor.Handle, BrightnessVcpCode,
                            IntPtr.Zero, out var current, out var max))
                        return NormalizeBrightness(current, max);
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
    ///     value against the monitor's reported VCP maximum. Failures (e.g.
    ///     monitors that don't support DDC/CI) are logged and ignored so they
    ///     never block the rest of a profile from being applied.
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
                    // Scale 0-100 to the monitor's real VCP range when it reports one.
                    var rawBrightness = targetBrightness;
                    if (GetVCPFeatureAndVCPFeatureReply(physicalMonitor.Handle, BrightnessVcpCode,
                            IntPtr.Zero, out _, out var max) && max > 0)
                        rawBrightness = ScaleBrightness(targetBrightness, max);

                    if (SetVCPFeature(physicalMonitor.Handle, BrightnessVcpCode, (uint)rawBrightness))
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

    private static int NormalizeBrightness(uint current, uint max)
    {
        // Some monitors report a maximum of 0; the DDC/CI convention is 0-100.
        if (max == 0)
            max = 100;

        return (int)Math.Clamp(Math.Round(current * 100.0 / max), 0, 100);
    }

    private static int ScaleBrightness(int brightness, uint max)
    {
        if (max == 0)
            return Math.Clamp(brightness, 0, 100);

        return (int)Math.Clamp(Math.Round(brightness * max / 100.0), 0, max);
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

    [DllImport("dxva2.dll", EntryPoint = "GetVCPFeatureAndVCPFeatureReply")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(
        IntPtr monitorHandle, byte vcpCode, IntPtr vcpCodeType,
        out uint currentValue, out uint maximumValue);

    [DllImport("dxva2.dll", EntryPoint = "SetVCPFeature")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetVCPFeature(IntPtr monitorHandle, byte vcpCode, uint newValue);
}
