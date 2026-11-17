using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Caliburn.Micro;
using DisplayFX.Global.Controllers;
using WindowsDisplayAPI;

namespace DisplayFX.Interface.BrightnessFlyout;

/// <summary>
///     View model for the Twinkle Tray-style "Adjust Brightness" flyout. Lists
///     every connected display with an independent brightness slider and opens the main window.
/// </summary>
public class BrightnessFlyoutViewModel : Screen, IDisposable
{
    private readonly MonitorBrightnessController _brightnessController;
    private readonly BrightnessPersistenceController _persistence;
    private string? _linkStatus;
    private readonly DisplayCache _displayCache;
    private readonly MonitorTopologyController? _topology;
    private bool _disposed;
    private bool _refreshing;

    public BrightnessFlyoutViewModel(
        MonitorBrightnessController brightnessController,
        DisplayCache displayCache,
        BrightnessPersistenceController brightnessPersistenceController, MonitorTopologyController? topology = null,
        bool deferInitialization = true)
    {
        _brightnessController = brightnessController;
        _persistence = brightnessPersistenceController;
        _displayCache = displayCache;
        _topology = topology;

        // Re-enumerate when opening the flyout so disconnected monitors disappear
        // and newly connected monitors have usable controls without restarting.
        var displays = deferInitialization ? Array.Empty<Display>() : GetConnectedDisplays();

        Monitors = new ObservableCollection<BrightnessMonitorViewModel>(
            displays
                .Select(display => new BrightnessMonitorViewModel(
                    display,
                    brightnessController,
                    brightnessApplied: brightnessPersistenceController.Remember,
                    name: brightnessPersistenceController.GetMonitorName(display), rememberedBrightness: brightnessPersistenceController.GetRememberedBrightness(display), deferInitialization: deferInitialization)));
        if (_topology != null) _topology.TopologyChanged += OnTopologyChanged;
        brightnessController.DisplayPowerChanged += OnDisplayPowerChanged;
        if (deferInitialization) Initialization = InitializeDisplaysAsync();
    }

    public Task Initialization { get; } = Task.CompletedTask;

    public ObservableCollection<BrightnessMonitorViewModel> Monitors { get; }

    internal void SeedConnectedDisplays(IEnumerable<Display> displays)
    {
        foreach (var display in displays)
            if (!Monitors.Any(row => IsSameDisplay(row.Display, display)))
                Monitors.Add(CreateDeferredRow(display));
    }

    private BrightnessMonitorViewModel CreateDeferredRow(Display display) => new(
        display, _brightnessController, brightnessApplied: _persistence.Remember,
        name: _persistence.GetMonitorName(display), rememberedBrightness: _persistence.GetRememberedBrightness(display), deferInitialization: true);

    private static bool IsSameDisplay(Display first, Display second) =>
        string.Equals(first.DevicePath, second.DevicePath, StringComparison.OrdinalIgnoreCase);

    private async Task InitializeDisplaysAsync()
    {
        // Let WPF lay out and paint the seeded rows before the background reconciliation.
        await Dispatcher.Yield(DispatcherPriority.Background);
        await RefreshDisplaysAsync(invalidateTransports: false);
    }

    public System.Action? MainAppRequested { get; set; }
    public void OpenMainApp() => MainAppRequested?.Invoke();

    public string? LinkStatus
    {
        get => _linkStatus;
        private set { _linkStatus = value; NotifyOfPropertyChange(); }
    }

    public async Task FlushPendingBrightnessAsync()
    {
        // Closing must wait for writes the user made, not for display discovery or reads.
        await Task.WhenAll(Monitors.Select(monitor => monitor.FlushPendingBrightnessAsync()));
    }

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
    private IReadOnlyList<Display> GetConnectedDisplays()
    {
        _displayCache.Refresh();
        return _displayCache.GetDisplays();
    }

    public Task RefreshDisplaysAsync() => RefreshDisplaysAsync(invalidateTransports: true);

    private async Task RefreshDisplaysAsync(bool invalidateTransports)
    {
        if (_disposed || _refreshing) return;
        _refreshing = true;
        try
        {
            // Enumerating after wake replaces stale logical monitor objects as well as their DDC ranges.
            var displays = await Task.Run(GetConnectedDisplays);
            if (_disposed) return;
            LinkStatus = null;
            if (invalidateTransports) _brightnessController.InvalidateTransports();
            foreach (var monitor in Monitors.ToList())
            {
                var display = displays.FirstOrDefault(display => IsSameDisplay(display, monitor.Display));
                if (display != null) await monitor.RefreshBrightnessAsync(display);
                else
                {
                    monitor.Dispose();
                    Monitors.Remove(monitor);
                }
                if (_disposed) return;
            }
            SeedConnectedDisplays(displays);
        }
        catch (Exception) { LinkStatus = "Display controls reconnect automatically after wake."; }
        finally { _refreshing = false; }
    }
}
