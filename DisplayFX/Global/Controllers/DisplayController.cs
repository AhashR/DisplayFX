using System;
using NLog;
using DisplayFX.Objects;
using DisplayFX.Objects.Entities;
using WindowsDisplayAPI;

namespace DisplayFX.Global.Controllers;

public class DisplayController
{
    private readonly ILogger _logger;
    private readonly DisplayWindowManager _windowManager;

    public DisplayController(ILogger logger, DisplayWindowManager windowManager)
    {
        _logger = logger;
        _windowManager = windowManager;
    }

    public string? LastError { get; private set; }

    public virtual bool UpdateColorSettings(Display display, ProfileSetting profileSetting,
        NvAPIWrapper.Display.Display? nvidiaMonitor)
    {
        try
        {
            LastError = null;
            var ramp = new DisplayGammaRamp(profileSetting.Brightness, profileSetting.Contrast, profileSetting.Gamma);
            // Capture the current values before a combined update so a GPU failure
            // does not leave the gamma preview applied while activation reports failure.
            var previousRamp = nvidiaMonitor != null ? display.GammaRamp : null;
            if (nvidiaMonitor != null && previousRamp == null)
                throw new InvalidOperationException("The previous color settings could not be read. No color changes were applied.");
            var previousVibrance = nvidiaMonitor?.DigitalVibranceControl.NormalizedLevel;
            _logger.Info("Applying GPU color settings for {0}: brightness={1}, contrast={2}, gamma={3}, vibrance={4}. No physical brightness commands.",
                display.DevicePath, profileSetting.Brightness, profileSetting.Contrast, profileSetting.Gamma, profileSetting.DigitalVibrance);
            ApplyWithRollback(
                () => display.GammaRamp = ramp,
                nvidiaMonitor == null ? null : () =>
                    nvidiaMonitor.DigitalVibranceControl.NormalizedLevel = (profileSetting.DigitalVibrance * 2.0) - 1.0,
                previousRamp == null ? null : () => display.GammaRamp = previousRamp,
                nvidiaMonitor == null || !previousVibrance.HasValue ? null : () =>
                    nvidiaMonitor.DigitalVibranceControl.NormalizedLevel = previousVibrance.Value);
            return true;
        }
        catch (Exception e)
        {
            var message = "Failed to update color settings.";

            _logger.Error(message);
            _logger.Error(e);

            LastError = message + " " + e.Message;
            return false;
        }
    }

    internal static void ApplyWithRollback(Action applyGamma, Action? applyVibrance,
        Action? restoreGamma, Action? restoreVibrance)
    {
        var vibranceAttempted = false;
        try
        {
            applyGamma();
            vibranceAttempted = applyVibrance != null;
            applyVibrance?.Invoke();
        }
        catch
        {
            // A disconnected monitor can also reject rollback; preserve the original failure.
            try { restoreGamma?.Invoke(); } catch { }
            if (vibranceAttempted)
                try { restoreVibrance?.Invoke(); } catch { }
            throw;
        }
    }
}
