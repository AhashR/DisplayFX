using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Caliburn.Micro;
using DisplayFX.Global.Controllers;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;

namespace DisplayFX.Interface.BrightnessFlyout;

/// <summary>
///     A single monitor row in the brightness flyout. Binds a slider to the
///     monitor's physical backlight (DDC/CI) and debounces rapid slider changes
///     so the DDC/CI write only happens once the user stops dragging.
/// </summary>
public class BrightnessMonitorViewModel : Screen
{
    private readonly MonitorBrightnessController _brightnessController;
    private readonly Action<BrightnessMonitorViewModel, double>? _brightnessChanged;
    private DispatcherTimer? _writeDebounce;
    private double _brightness;

    public BrightnessMonitorViewModel(
        Display display,
        MonitorBrightnessController brightnessController,
        Action<BrightnessMonitorViewModel, double>? brightnessChanged = null)
    {
        Display = display;
        _brightnessController = brightnessController;
        _brightnessChanged = brightnessChanged;

        Name = ResolveName(display);

        var current = brightnessController.GetBrightness(display);
        SupportsBrightness = current.HasValue || brightnessController.SupportsBrightness(display);
        _brightness = current ?? 50;
    }

    public Display Display { get; }

    public string Name { get; }

    /// <summary>
    ///     Whether the monitor reported a readable brightness over DDC/CI.
    /// </summary>
    public bool SupportsBrightness { get; }

    public double Brightness
    {
        get => _brightness;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, 0, 100));
            if (clamped.Equals(_brightness))
                return;

            _brightness = clamped;
            NotifyOfPropertyChange();

            _brightnessChanged?.Invoke(this, clamped);
            ScheduleWrite();
        }
    }

    /// <summary>
    ///     Applies a brightness value immediately without re-broadcasting the
    ///     change (used when monitors are linked together).
    /// </summary>
    public void ApplyBrightness(double value)
    {
        var clamped = Math.Round(Math.Clamp(value, 0, 100));
        if (clamped.Equals(_brightness))
            return;

        _brightness = clamped;
        NotifyOfPropertyChange(nameof(Brightness));
        _brightnessController.SetBrightness(Display, (int)clamped);
    }

    private void ScheduleWrite()
    {
        if (_writeDebounce == null)
        {
            _writeDebounce = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _writeDebounce.Tick += (_, _) =>
            {
                _writeDebounce.Stop();
                _brightnessController.SetBrightness(Display, (int)_brightness);
            };
        }

        _writeDebounce.Stop();
        _writeDebounce.Start();
    }

    private static string ResolveName(Display display)
    {
        // Prefer the EDID-derived friendly name (e.g. "MSI MAG251RX"), the same
        // source the main window uses when building its monitor list.
        try
        {
            var target = PathDisplayTarget.GetDisplayTargets()
                .FirstOrDefault(t =>
                    string.Equals(t.DevicePath, display.DevicePath, StringComparison.OrdinalIgnoreCase));

            if (target != null && !string.IsNullOrWhiteSpace(target.FriendlyName))
                return target.FriendlyName;
        }
        catch
        {
            // Fall through to the next candidate.
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(display.DisplayName))
                return display.DisplayName;
        }
        catch
        {
            // Fall through to the next candidate.
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(display.ScreenName))
                return display.ScreenName;
        }
        catch
        {
            // Fall through to the default.
        }

        return "Monitor";
    }
}
