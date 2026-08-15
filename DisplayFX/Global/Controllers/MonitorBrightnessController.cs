using System;
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
    private readonly ILogger _logger;

    public MonitorBrightnessController(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Sets the hardware brightness (0-100) of the given display. Failures
    ///     (e.g. monitors that don't support DDC/CI) are logged and ignored so
    ///     they never block the rest of a profile from being applied.
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
                var targetBrightness = (uint)Math.Clamp(brightness, 0, 100);

                foreach (var physicalMonitor in physicalMonitors)
                {
                    if (SetMonitorBrightness(physicalMonitor.Handle, targetBrightness))
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
}
