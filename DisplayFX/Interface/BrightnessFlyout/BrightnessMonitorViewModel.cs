using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Caliburn.Micro;
using DisplayFX.Global.Controllers;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;

namespace DisplayFX.Interface.BrightnessFlyout;

/// <summary>Coalesces slider input and serializes slow monitor writes away from the UI thread.</summary>
public class BrightnessMonitorViewModel : Screen, IDisposable
{
    private readonly MonitorBrightnessController _brightnessController;
    private readonly Action<BrightnessMonitorViewModel, double>? _brightnessChanged;
    private readonly Action<Display, int>? _brightnessApplied;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _writeDebounce;
    private readonly DispatcherTimer _recoveryTimer;
    private Task _worker = Task.CompletedTask;
    private int? _pendingBrightness;
    private bool _writeInFlight;
    private bool _disposed;
    private double _brightness;
    private string _statusMessage = "";
    private bool _hasError;
    private bool _refreshing;
    private bool _hasKnownBrightness;

    public BrightnessMonitorViewModel(Display display, MonitorBrightnessController brightnessController,
        Action<BrightnessMonitorViewModel, double>? brightnessChanged = null,
        Action<Display, int>? brightnessApplied = null, string? name = null, int? rememberedBrightness = null)
    {
        Display = display;
        _brightnessController = brightnessController;
        _brightnessChanged = brightnessChanged;
        _brightnessApplied = brightnessApplied;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        Name = string.IsNullOrWhiteSpace(name) ? ResolveName(display) : name;
        var current = brightnessController.GetBrightness(display);
        SupportsBrightness = !brightnessController.IsDisplaySleeping && (current.HasValue || brightnessController.SupportsBrightness(display));
        var known = current ?? brightnessController.GetLastKnownBrightness(display) ?? rememberedBrightness;
        _brightness = known ?? 0;
        _hasKnownBrightness = known.HasValue;
        HasError = !current.HasValue && !brightnessController.IsDisplaySleeping;
        StatusMessage = current.HasValue ? "" : "Brightness unavailable. Check the monitor connection and DDC/CI setting.";
        _writeDebounce = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        { Interval = TimeSpan.FromMilliseconds(120) };
        _writeDebounce.Tick += OnWriteTick;
        _recoveryTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromSeconds(3) };
        _recoveryTimer.Tick += OnRecoveryTick;
        _recoveryTimer.Start();
        brightnessController.StatusChanged += OnStatusChanged;
        brightnessController.DisplayPowerChanged += OnDisplayPowerChanged;
    }

    public Display Display { get; private set; }
    public string Name { get; }
    public bool SupportsBrightness { get; private set; }
    public string BrightnessText => _hasKnownBrightness ? $"{_brightness:0}%" : "—";
    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; NotifyOfPropertyChange(); }
    }
    public bool HasError
    {
        get => _hasError;
        private set { _hasError = value; NotifyOfPropertyChange(); }
    }
    public double Brightness
    {
        get => _brightness;
        set => QueueBrightness(value, true);
    }

    public void ApplyBrightness(double value) => QueueBrightness(value, false);

    private void QueueBrightness(double value, bool broadcast)
    {
        if (_disposed || !SupportsBrightness || !double.IsFinite(value)) return;
        var target = (int)Math.Round(Math.Clamp(value, 0, 100));
        if (target == _brightness && !HasError) return;
        _brightness = target;
        _hasKnownBrightness = true;
        _pendingBrightness = target;
        HasError = false;
        StatusMessage = "";
        NotifyOfPropertyChange(nameof(Brightness));
        NotifyOfPropertyChange(nameof(BrightnessText));
        if (broadcast) _brightnessChanged?.Invoke(this, target);
        // Throttle, rather than restarting a debounce indefinitely during a long drag.
        if (!_writeInFlight && !_writeDebounce.IsEnabled) _writeDebounce.Start();
    }

    private void OnWriteTick(object? sender, EventArgs args)
    {
        _writeDebounce.Stop();
        StartWorker();
    }

    private void StartWorker()
    {
        if (_writeInFlight || !_pendingBrightness.HasValue || _disposed) return;
        _writeInFlight = true;
        _worker = WritePendingAsync();
    }

    private async Task WritePendingAsync()
    {
        try
        {
            while (_pendingBrightness.HasValue && !_disposed)
            {
                var target = _pendingBrightness.Value;
                _pendingBrightness = null;
                var applied = await Task.Run(() => _brightnessController.SetBrightness(Display, target));
                // A transient busy monitor gets one automatic retry. Newer input takes priority.
                if (!applied && !_pendingBrightness.HasValue && !_disposed)
                {
                    await Task.Delay(180);
                    if (!_pendingBrightness.HasValue && !_disposed)
                        applied = await Task.Run(() => _brightnessController.SetBrightness(Display, target));
                }
                if (applied) _brightnessApplied?.Invoke(Display, target);
                if (!_pendingBrightness.HasValue && !_disposed)
                {
                    HasError = !applied;
                    StatusMessage = applied ? "" : "Brightness change failed. Check the monitor connection and DDC/CI setting.";
                }
                if (_pendingBrightness.HasValue) await Task.Delay(100);
            }
        }
        catch (Exception)
        {
            HasError = true;
            StatusMessage = "Could not apply or remember brightness. Check the monitor connection and settings folder.";
        }
        finally { _writeInFlight = false; }
    }

    /// <summary>Includes in-flight and final pending input, even when closing before the timer fires.</summary>
    public async Task FlushPendingBrightnessAsync()
    {
        _writeDebounce.Stop();
        StartWorker();
        await _worker;
    }

    private async void OnRecoveryTick(object? sender, EventArgs args)
    {
        if (HasError || !SupportsBrightness) await RefreshBrightnessAsync();
    }

    private void OnDisplayPowerChanged(bool awake)
    {
        void Update()
        {
            if (_disposed) return;
            if (!awake)
            {
                SupportsBrightness = false;
                HasError = false;
                StatusMessage = "Display asleep";
                NotifyOfPropertyChange(nameof(SupportsBrightness));
            }
            else _ = RefreshBrightnessAsync();
        }
        if (_dispatcher.CheckAccess()) Update(); else _dispatcher.BeginInvoke(Update);
    }

    public async Task RefreshBrightnessAsync(Display? updatedDisplay = null)
    {
        if (_disposed || _refreshing || _writeInFlight || _pendingBrightness.HasValue || _brightnessController.IsDisplaySleeping) return;
        if (updatedDisplay != null && string.Equals(updatedDisplay.DevicePath, Display.DevicePath, StringComparison.OrdinalIgnoreCase)) Display = updatedDisplay;
        _refreshing = true;
        try
        {
            var current = await Task.Run(() => _brightnessController.GetBrightness(Display));
            if (_disposed || _writeInFlight || _pendingBrightness.HasValue || _brightnessController.IsDisplaySleeping) return;
            SupportsBrightness = current.HasValue;
            HasError = !current.HasValue;
            StatusMessage = current.HasValue ? "" : "Monitor is unavailable. Controls reconnect automatically when it wakes.";
            if (current.HasValue) { _brightness = current.Value; _hasKnownBrightness = true; }
            NotifyOfPropertyChange(nameof(SupportsBrightness));
            NotifyOfPropertyChange(nameof(Brightness));
            NotifyOfPropertyChange(nameof(BrightnessText));
        }
        catch (Exception) { HasError = true; StatusMessage = "Monitor is unavailable. Controls reconnect automatically when it wakes."; }
        finally { _refreshing = false; }
    }

    private void OnStatusChanged(Display display, BrightnessStatus status)
    {
        if (!string.Equals(display.DevicePath, Display.DevicePath, StringComparison.OrdinalIgnoreCase)) return;
        void Update()
        {
            if (_disposed || _refreshing || _writeInFlight || _pendingBrightness.HasValue || _brightnessController.IsDisplaySleeping) return;
            HasError = status.IsError;
            StatusMessage = status.IsError ? status.Message : "";
            if (!status.IsError && status.Brightness.HasValue)
            {
                _brightness = status.Brightness.Value;
                _hasKnownBrightness = true;
                SupportsBrightness = true;
                NotifyOfPropertyChange(nameof(SupportsBrightness));
                NotifyOfPropertyChange(nameof(Brightness));
                NotifyOfPropertyChange(nameof(BrightnessText));
            }
        }
        if (_dispatcher.CheckAccess()) Update();
        else _dispatcher.BeginInvoke(Update);
    }

    public void Dispose()
    {
        _disposed = true;
        _pendingBrightness = null;
        _writeDebounce.Stop();
        _writeDebounce.Tick -= OnWriteTick;
        _recoveryTimer.Stop();
        _recoveryTimer.Tick -= OnRecoveryTick;
        _brightnessController.StatusChanged -= OnStatusChanged;
        _brightnessController.DisplayPowerChanged -= OnDisplayPowerChanged;
    }

    private static string ResolveName(Display display)
    {
        try
        {
            var target = PathDisplayTarget.GetDisplayTargets().FirstOrDefault(t =>
                string.Equals(t.DevicePath, display.DevicePath, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(target?.FriendlyName)) return target.FriendlyName;
        }
        catch { }
        try { if (!string.IsNullOrWhiteSpace(display.DisplayName)) return display.DisplayName; }
        catch { }
        try { if (!string.IsNullOrWhiteSpace(display.ScreenName)) return display.ScreenName; }
        catch { }
        return "Monitor";
    }
}
