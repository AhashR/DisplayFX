using System;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using NLog;

namespace DisplayFX.Global.Controllers;

public sealed class MonitorTopologyController : IDisposable
{
    private readonly ILogger _logger;
    private Dispatcher? _dispatcher;
    private DispatcherTimer? _debounceTimer;
    private bool _watching;
    private bool _disposed;

    public MonitorTopologyController(ILogger logger)
    {
        _logger = logger;
    }

    public event EventHandler? TopologyChanged;

    public void Start()
    {
        if (_watching || _disposed) return;
        _dispatcher = Application.Current?.Dispatcher;
        if (_dispatcher == null || _dispatcher.HasShutdownStarted) return;

        try
        {
            _debounceTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(750)
            };
            _debounceTimer.Tick += OnDebounceTimerTick;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _watching = true;
        }
        catch (Exception exception)
        {
            _logger.Warn(exception, "Windows display notifications are unavailable; use Refresh displays to update monitors.");
            _debounceTimer?.Stop();
            if (_debounceTimer != null)
                _debounceTimer.Tick -= OnDebounceTimerTick;
            _debounceTimer = null;
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed || _dispatcher == null || _dispatcher.HasShutdownStarted) return;
        try
        {
            // Windows can deliver several notifications for one docking or resolution change.
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed) return;
                _debounceTimer?.Stop();
                _debounceTimer?.Start();
            }), DispatcherPriority.Background);
        }
        catch (InvalidOperationException exception)
        {
            _logger.Debug(exception, "Display refresh was skipped while the application was closing.");
        }
    }

    private void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _debounceTimer?.Stop();
        if (_disposed) return;
        try
        {
            TopologyChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.Warn(exception, "Failed to update monitor controls after a display change.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_watching)
        {
            try
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            }
            catch (Exception exception)
            {
                _logger.Debug(exception, "Windows display notifications were already stopped.");
            }
            _watching = false;
        }

        void StopTimer()
        {
            _debounceTimer?.Stop();
            if (_debounceTimer != null)
                _debounceTimer.Tick -= OnDebounceTimerTick;
            _debounceTimer = null;
        }

        if (_dispatcher == null || _dispatcher.CheckAccess())
            StopTimer();
        else if (!_dispatcher.HasShutdownStarted)
        {
            try
            {
                _dispatcher.BeginInvoke(new Action(StopTimer));
            }
            catch (InvalidOperationException exception)
            {
                _logger.Debug(exception, "Display refresh timer was already stopped during shutdown.");
            }
        }
        TopologyChanged = null;
    }
}
