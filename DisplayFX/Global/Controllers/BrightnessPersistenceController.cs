using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DisplayFX.Objects.Entities;
using NLog;
using WindowsDisplayAPI;
using Monitor = DisplayFX.Objects.Entities.Monitor;

namespace DisplayFX.Global.Controllers;

/// <summary>
///     Persists user-requested hardware brightness and restores it after startup.
/// </summary>
public class BrightnessPersistenceController
{
    private static readonly TimeSpan[] StartupRestoreDelays =
    {
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15)
    };

    private readonly DataController _dataController;
    private readonly DisplayCache _displayCache;
    private readonly ILogger _logger;
    private readonly MonitorBrightnessController _monitorBrightnessController;
    private readonly Dictionary<Monitor, (int Brightness, CancellationTokenSource Cancellation)> _startupRestores = new();
    private Computer? _computer;
    private bool _startupRestoreStarted;

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
        foreach (var restore in _startupRestores.Values)
            restore.Cancellation.Cancel();
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
    public DisplayFX.Objects.Entities.Monitor? GetMonitor(Display display) => _computer?.Monitors.FirstOrDefault(monitor => string.Equals(monitor.DisplayDevicePath, display.DevicePath, StringComparison.OrdinalIgnoreCase));
    internal void CancelStartupRestore(Display display)
    {
        if (GetMonitor(display) is { } monitor && _startupRestores.TryGetValue(monitor, out var restore))
            restore.Cancellation.Cancel();
    }

    internal async Task RestoreOnStartupAsync()
    {
        if (_computer is null || _startupRestoreStarted)
            return;

        _startupRestoreStarted = true;
        foreach (var monitor in _computer.Monitors)
        {
            if (monitor.LastBrightness is int brightness)
                _startupRestores.Add(monitor, (brightness, new CancellationTokenSource()));
        }

        try
        {
            foreach (var delay in StartupRestoreDelays)
            {
                if (_startupRestores.Count == 0)
                    break;

                await Task.Delay(delay);
                if (_startupRestores.Values.All(restore => restore.Cancellation.IsCancellationRequested))
                    break;

                IReadOnlyList<Display> displays;
                try
                {
                    displays = await Task.Run(() =>
                    {
                        _displayCache.Refresh();
                        return _displayCache.GetDisplays();
                    });
                }
                catch (Exception exception)
                {
                    _logger.Warn(exception, "Could not enumerate displays for brightness restoration.");
                    continue;
                }

                foreach (var (monitor, (brightness, cancellation)) in _startupRestores.ToArray())
                {
                    var display = displays.FirstOrDefault(candidate =>
                        string.Equals(candidate.DevicePath, monitor.DisplayDevicePath, StringComparison.OrdinalIgnoreCase));
                    if (!cancellation.IsCancellationRequested && (display is null || !await Task.Run(() =>
                            _monitorBrightnessController.TryRestoreBrightness(display, brightness, cancellation.Token))))
                        continue;

                    _startupRestores.Remove(monitor);
                    cancellation.Dispose();
                }
            }
        }
        catch (Exception exception)
        {
            _logger.Warn(exception, "Failed to restore remembered monitor brightness.");
        }
        finally
        {
            foreach (var restore in _startupRestores.Values)
                restore.Cancellation.Dispose();
            _startupRestores.Clear();
        }
    }

    public void Remember(Display display, int brightness)
    {
        if (_computer is null)
            return;
        if (_monitorBrightnessController.GetLastKnownBrightness(display) is int latest && latest != brightness)
            return; // A delayed UI completion must not replace a newer accepted value.

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
