using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using DisplayFX.Objects;
using DisplayFX.Objects.Entities;
using WindowsDisplayAPI;

namespace DisplayFX.Global.Controllers;

public class DisplayController
{
    private readonly ILogger _logger;
    private readonly DisplayWindowManager _windowManager;
    private readonly Dictionary<string, DisplayGammaRamp> _lastAppliedColors = new(StringComparer.OrdinalIgnoreCase);
    public sealed record ColorSnapshot(DisplayGammaRamp Ramp, double? Vibrance);
    public virtual ColorSnapshot CaptureColors(Display display, NvAPIWrapper.Display.Display? nvidia) =>
        new(display.GammaRamp ?? throw new InvalidOperationException("The current gamma curve cannot be read."), nvidia?.DigitalVibranceControl.NormalizedLevel);
    public virtual bool RestoreColors(Display display, NvAPIWrapper.Display.Display? nvidia, ColorSnapshot snapshot)
    {
        try
        {
            LastError = null;
            if (DisplayCapabilitiesController.IsAdvancedColorEnabled(display) == true) throw new InvalidOperationException("HDR/advanced color is active. Standard gamma restoration is unavailable.");
            var previous = CaptureColors(display, nvidia);
            ApplyWithRollback(() => display.GammaRamp = snapshot.Ramp,
                nvidia != null && snapshot.Vibrance.HasValue ? () => nvidia.DigitalVibranceControl.NormalizedLevel = snapshot.Vibrance.Value : null,
                () => display.GammaRamp = previous.Ramp,
                nvidia != null && previous.Vibrance.HasValue ? () => nvidia.DigitalVibranceControl.NormalizedLevel = previous.Vibrance.Value : null);
            _lastAppliedColors[display.DevicePath] = snapshot.Ramp;
            return true;
        }
        catch (Exception ex) { LastError = "Could not restore colors. " + ex.Message; _logger.Warn(ex, LastError); return false; }
    }
    public virtual bool? HasExternalColorChange(Display display)
    {
        if (!_lastAppliedColors.TryGetValue(display.DevicePath, out var expected)) return null;
        try { var actual = display.GammaRamp; return actual == null ? null : !actual.Red.SequenceEqual(expected.Red) || !actual.Green.SequenceEqual(expected.Green) || !actual.Blue.SequenceEqual(expected.Blue); }
        catch { return null; }
    }

    public DisplayController(ILogger logger, DisplayWindowManager windowManager)
    {
        _logger = logger;
        _windowManager = windowManager;
    }

    public string? LastError { get; private set; }
    public virtual bool UpdateColorSettingsWithTransition(Display display, ProfileSetting settings, NvAPIWrapper.Display.Display? nvidia, int duration, Func<bool> connectionUnchanged)
    {
        if (duration <= 0) return UpdateColorSettings(display, settings, nvidia);
        ColorSnapshot? initial = null;
        try
        {
            LastError = null;
            if (nvidia == null && settings.DigitalVibrance != .5) throw new InvalidOperationException("NVIDIA Digital Vibrance is unavailable.");
            if (DisplayCapabilitiesController.IsAdvancedColorEnabled(display) == true) throw new InvalidOperationException("HDR/advanced color is active. Standard gamma transitions are unavailable.");
            initial = CaptureColors(display, nvidia);
            var target = CreateRamp(settings);
            var milliseconds = Math.Clamp(duration, 1, 5000);
            var steps = Math.Clamp(milliseconds / 100, 1, 20);
            for (var step = 1; step <= steps; step++)
            {
                if (!connectionUnchanged()) throw new InvalidOperationException("Display connection changed during the transition. Reapply the intended profile after reconnection.");
                if (DisplayCapabilitiesController.IsAdvancedColorEnabled(display) == true) throw new InvalidOperationException("Advanced color became active during the transition; legacy gamma writes were stopped.");
                var fraction = step / (double)steps;
                var ramp = BlendRamps(initial.Ramp, target, fraction);
                var vibrance = initial.Vibrance + (((settings.DigitalVibrance * 2) - 1) - initial.Vibrance) * fraction;
                ApplyWithRollback(() => display.GammaRamp = ramp,
                    nvidia != null && vibrance.HasValue ? () => nvidia.DigitalVibranceControl.NormalizedLevel = vibrance.Value : null,
                    () => display.GammaRamp = initial.Ramp,
                    nvidia != null && initial.Vibrance.HasValue ? () => nvidia.DigitalVibranceControl.NormalizedLevel = initial.Vibrance.Value : null);
                _lastAppliedColors[display.DevicePath] = ramp;
                if (step < steps) System.Threading.Thread.Sleep(milliseconds / steps);
            }
            return true;
        }
        catch (Exception ex)
        {
            // A stopped transition must not leave its last intermediate ramp behind.
            // Never send rollback writes to a replaced output or an active HDR route.
            _lastAppliedColors.Remove(display.DevicePath);
            try
            {
                if (initial != null && connectionUnchanged() && DisplayCapabilitiesController.IsAdvancedColorEnabled(display) != true)
                    RestoreColors(display, nvidia, initial);
            }
            catch { }
            LastError = "Color transition failed. " + ex.Message; _logger.Warn(ex, LastError); return false;
        }
    }
    internal static DisplayGammaRamp BlendRamps(DisplayGammaRamp initial, DisplayGammaRamp target, double fraction)
    {
        ushort[] Blend(ushort[] from, ushort[] to) => from.Select((value, index) => (ushort)Math.Clamp(Math.Round(value + (to[index] - value) * Math.Clamp(fraction, 0, 1)), 0, ushort.MaxValue)).ToArray();
        return new(Blend(initial.Red, target.Red), Blend(initial.Green, target.Green), Blend(initial.Blue, target.Blue));
    }

    public virtual bool UpdateColorSettings(Display display, ProfileSetting profileSetting,
        NvAPIWrapper.Display.Display? nvidiaMonitor)
    {
        try
        {
            LastError = null;
            if (nvidiaMonitor == null && profileSetting.DigitalVibrance != 0.5)
                throw new InvalidOperationException("NVIDIA Digital Vibrance is unavailable. Refresh displays after the driver update, or restart DisplayFX, then apply the profile again.");
            if (DisplayCapabilitiesController.IsAdvancedColorEnabled(display) == true)
                throw new InvalidOperationException("HDR/advanced color is active. Standard gamma controls are unavailable; physical backlight controls remain separate.");
            var ramp = CreateRamp(profileSetting);
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
            _lastAppliedColors[display.DevicePath] = ramp;
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

    internal static DisplayGammaRamp CreateRamp(ProfileSetting settings) => new(settings.Brightness, settings.Contrast, settings.Gamma);

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
