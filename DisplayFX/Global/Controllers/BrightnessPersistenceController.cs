using System;
using System.Linq;
using DisplayFX.Objects.Entities;
using NLog;
using WindowsDisplayAPI;

namespace DisplayFX.Global.Controllers;

/// <summary>
///     Persists user-requested hardware brightness values and restores them
///     before the main window is displayed.
/// </summary>
public class BrightnessPersistenceController
{
    private readonly DataController _dataController;
    private readonly DisplayCache _displayCache;
    private readonly ILogger _logger;
    private readonly MonitorBrightnessController _monitorBrightnessController;
    private Computer? _computer;

    public BrightnessPersistenceController(
        DataController dataController,
        DisplayCache displayCache,
        MonitorBrightnessController monitorBrightnessController,
        ILogger logger)
    {
        _dataController = dataController;
        _displayCache = displayCache;
        _monitorBrightnessController = monitorBrightnessController;
        _logger = logger;
    }

    /// <summary>
    ///     Connects brightness changes to the Computer instance owned by the
    ///     shell so subsequent saves cannot overwrite the remembered values.
    /// </summary>
    public void Attach(Computer computer)
    {
        _computer = computer;
    }

    public void Detach()
    {
        _computer = null;
    }

    public bool IsLinked
    {
        get => _computer?.IsBrightnessLinked == true;
        set
        {
            if (_computer is null || _computer.IsBrightnessLinked == value)
                return;
            var previous = _computer.IsBrightnessLinked;
            _computer.IsBrightnessLinked = value;
            try
            {
                _dataController.Write(_computer);
            }
            catch (Exception exception)
            {
                _computer.IsBrightnessLinked = previous;
                _logger.Warn(exception, "Failed to save linked brightness controls.");
                throw;
            }
        }
    }

    public string? GetMonitorName(Display display) => _computer?.Monitors.FirstOrDefault(monitor =>
        string.Equals(monitor.DisplayDevicePath, display.DevicePath, StringComparison.OrdinalIgnoreCase))?.DisplayName;
    public int? GetRememberedBrightness(Display display) => _computer?.Monitors.FirstOrDefault(monitor =>
        string.Equals(monitor.DisplayDevicePath, display.DevicePath, StringComparison.OrdinalIgnoreCase))?.LastBrightness;

    public void RestoreOnStartup()
    {
        var loadResult = _dataController.Load();
        if (loadResult.IsFailed || !loadResult.Value.IsRememberBrightnessOnStart)
            return;

        try
        {
            var displays = _displayCache.GetDisplays();

            foreach (var monitor in loadResult.Value.Monitors.Where(monitor => monitor.LastBrightness.HasValue))
            {
                var display = displays.FirstOrDefault(candidate =>
                    string.Equals(candidate.DevicePath, monitor.DisplayDevicePath,
                        StringComparison.OrdinalIgnoreCase));

                if (display is not null)
                    _monitorBrightnessController.SetBrightness(display, monitor.LastBrightness!.Value);
            }
        }
        catch (Exception exception)
        {
            _logger.Warn(exception, "Failed to restore remembered monitor brightness.");
        }
    }

    public void Remember(Display display, int brightness)
    {
        if (_computer is null)
            return;

        var monitor = _computer.Monitors.FirstOrDefault(candidate =>
            string.Equals(candidate.DisplayDevicePath, display.DevicePath,
                StringComparison.OrdinalIgnoreCase));

        if (monitor is null)
            return;

        var clampedBrightness = Math.Clamp(brightness, 0, 100);
        if (monitor.LastBrightness == clampedBrightness)
            return;

        var previousBrightness = monitor.LastBrightness;
        monitor.LastBrightness = clampedBrightness;

        try
        {
            _dataController.Write(_computer);
        }
        catch (Exception exception)
        {
            monitor.LastBrightness = previousBrightness;
            _logger.Warn(exception, "Failed to remember monitor brightness.");
        }
    }
}
