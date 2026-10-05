using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Caliburn.Micro;
using DisplayFX.Global.Controllers;

namespace DisplayFX.Interface.BrightnessFlyout;

/// <summary>
///     View model for the Twinkle Tray-style "Adjust Brightness" flyout. Lists
///     every connected display with a live brightness slider and supports linking
///     monitors, turning them off, and opening the app settings.
/// </summary>
public class BrightnessFlyoutViewModel : Screen
{
    private readonly MonitorBrightnessController _brightnessController;
    private readonly BrightnessPersistenceController _persistence;
    private bool _isLinked;
    private string? _linkStatus;
    private readonly DisplayCache _displayCache;
    private readonly MonitorTopologyController? _topology;
    private bool _disposed;
    private bool _refreshing;

    public BrightnessFlyoutViewModel(
        MonitorBrightnessController brightnessController,
        DisplayCache displayCache,
        BrightnessPersistenceController brightnessPersistenceController, MonitorTopologyController? topology = null)
    {
        _brightnessController = brightnessController;
        _persistence = brightnessPersistenceController;
        _displayCache = displayCache;
        _topology = topology;
        _isLinked = brightnessPersistenceController.IsLinked;

        // Re-enumerate when opening the flyout so disconnected monitors disappear
        // and newly connected monitors have usable controls without restarting.
        displayCache.Refresh();

        Monitors = new ObservableCollection<BrightnessMonitorViewModel>(
            displayCache.GetDisplays()
                .Select(display => new BrightnessMonitorViewModel(
                    display,
                    brightnessController,
                    OnMonitorBrightnessChanged,
                    brightnessPersistenceController.Remember,
                    brightnessPersistenceController.GetMonitorName(display), brightnessPersistenceController.GetRememberedBrightness(display))));
        if (_topology != null) _topology.TopologyChanged += OnTopologyChanged;
        brightnessController.DisplayPowerChanged += OnDisplayPowerChanged;
    }

    public ObservableCollection<BrightnessMonitorViewModel> Monitors { get; }

    /// <summary>
    ///     Invoked when the user clicks the settings gear. The view wires this to
    ///     the main window's settings dialog.
    /// </summary>
    public System.Action? SettingsRequested { get; set; }
    public System.Action? MainAppRequested { get; set; }
    public void OpenMainApp() => MainAppRequested?.Invoke();

    /// <summary>
    ///     Invoked when the flyout should close (e.g. after turning monitors off).
    /// </summary>
    public System.Action? CloseRequested { get; set; }

    /// <summary>
    ///     When linked, changing one monitor's brightness moves all the others to
    ///     the same value. Restoring the preference does not change monitor brightness.
    /// </summary>
    public bool IsLinked
    {
        get => _isLinked;
        set
        {
            if (_isLinked == value)
                return;

            try
            {
                _persistence.IsLinked = value;
                _isLinked = value;
                LinkStatus = null;
            }
            catch (Exception)
            {
                LinkStatus = "Could not save linked controls. Check access to your settings folder.";
            }
            NotifyOfPropertyChange();
        }
    }

    public string? LinkStatus
    {
        get => _linkStatus;
        private set { _linkStatus = value; NotifyOfPropertyChange(); }
    }

    public async void TurnOffMonitors()
    {
        var displays = Monitors.Select(monitor => monitor.Display).ToList();
        await FlushPendingBrightnessAsync();
        CloseRequested?.Invoke();
        await Task.Delay(350);
        await Task.Run(() => _brightnessController.TurnOffDisplays(displays));
    }

    public void OpenSettings()
    {
        SettingsRequested?.Invoke();
    }

    public Task FlushPendingBrightnessAsync() => Task.WhenAll(Monitors.Select(monitor => monitor.FlushPendingBrightnessAsync()));

    public void Dispose()
    {
        _disposed = true;
        if (_topology != null) _topology.TopologyChanged -= OnTopologyChanged;
        if (_brightnessController != null) _brightnessController.DisplayPowerChanged -= OnDisplayPowerChanged;
        foreach (var monitor in Monitors)
            monitor.Dispose();
    }

    private async void OnTopologyChanged(object? sender, EventArgs args) => await RefreshDisplaysAsync();
    private async void OnDisplayPowerChanged(bool awake)
    {
        if (awake) { await Task.Delay(600); await RefreshDisplaysAsync(); }
    }
    public async Task RefreshDisplaysAsync()
    {
        if (_disposed || _refreshing) return;
        _refreshing = true;
        try
        {
            // Enumerating after wake replaces stale logical monitor objects as well as their DDC ranges.
            _displayCache.Refresh();
            _brightnessController.InvalidateTransports();
            var displays = _displayCache.GetDisplays();
            foreach (var monitor in Monitors.ToList())
            {
                var display = displays.FirstOrDefault(display => string.Equals(display.DevicePath, monitor.Display.DevicePath, StringComparison.OrdinalIgnoreCase));
                if (display != null) await monitor.RefreshBrightnessAsync(display);
                if (_disposed) return;
            }
            foreach (var display in displays.Where(display => Monitors.All(row => !string.Equals(row.Display.DevicePath, display.DevicePath, StringComparison.OrdinalIgnoreCase))))
            {
                if (_disposed) return;
                Monitors.Add(new BrightnessMonitorViewModel(display, _brightnessController, OnMonitorBrightnessChanged,
                    _persistence.Remember, _persistence.GetMonitorName(display), _persistence.GetRememberedBrightness(display)));
            }
        }
        catch (Exception) { LinkStatus = "Display controls reconnect automatically after wake."; }
        finally { _refreshing = false; }
    }

    private void OnMonitorBrightnessChanged(BrightnessMonitorViewModel source, double value)
    {
        if (!_isLinked)
            return;

        foreach (var monitor in Monitors)
        {
            if (!ReferenceEquals(monitor, source) && monitor.SupportsBrightness)
                monitor.ApplyBrightness(value);
        }
    }

}
