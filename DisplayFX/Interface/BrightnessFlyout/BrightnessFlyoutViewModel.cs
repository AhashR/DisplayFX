using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
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
    private readonly DisplayCache _displayCache;
    private bool _isLinked;

    public BrightnessFlyoutViewModel(MonitorBrightnessController brightnessController, DisplayCache displayCache)
    {
        _brightnessController = brightnessController;
        _displayCache = displayCache;

        Monitors = new ObservableCollection<BrightnessMonitorViewModel>(
            displayCache.GetDisplays()
                .Select(display => new BrightnessMonitorViewModel(display, brightnessController, OnMonitorBrightnessChanged)));
    }

    public ObservableCollection<BrightnessMonitorViewModel> Monitors { get; }

    /// <summary>
    ///     Invoked when the user clicks the settings gear. The view wires this to
    ///     the main window's settings dialog.
    /// </summary>
    public System.Action? SettingsRequested { get; set; }

    /// <summary>
    ///     Invoked when the flyout should close (e.g. after turning monitors off).
    /// </summary>
    public System.Action? CloseRequested { get; set; }

    /// <summary>
    ///     When linked, changing one monitor's brightness moves all the others to
    ///     the same value. Enabling the link normalizes every monitor first.
    /// </summary>
    public bool IsLinked
    {
        get => _isLinked;
        set
        {
            if (_isLinked == value)
                return;

            _isLinked = value;
            NotifyOfPropertyChange();

            if (value)
                NormalizeAcrossMonitors();
        }
    }

    public void TurnOffMonitors()
    {
        var displays = Monitors.Select(monitor => monitor.Display).ToList();
        CloseRequested?.Invoke();

        // Let the flyout hide before the backlights switch off.
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _brightnessController.TurnOffDisplays(displays);
        };
        timer.Start();
    }

    public void OpenSettings()
    {
        SettingsRequested?.Invoke();
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

    private void NormalizeAcrossMonitors()
    {
        var supported = Monitors.Where(monitor => monitor.SupportsBrightness).ToList();
        if (supported.Count < 2)
            return;

        var average = Math.Round(supported.Average(monitor => monitor.Brightness));
        foreach (var monitor in supported)
            monitor.ApplyBrightness(average);
    }
}
