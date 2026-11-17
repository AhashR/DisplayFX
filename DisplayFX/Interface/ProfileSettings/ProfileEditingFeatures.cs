using System;
using System.Collections.Generic;
using System.Globalization;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Interface.ProfileSettings;

public partial class ProfileSettingViewModel
{
    private readonly Stack<ProfileSetting> _undoColors = new();
    private readonly Stack<ProfileSetting> _redoColors = new();
    private readonly Dictionary<string, string> _invalidInputs = new();
    public bool CanUndoColor => _undoColors.Count > 0;
    public bool CanRedoColor => _redoColors.Count > 0;
    public bool HasInputErrors => _invalidInputs.Count > 0;
    public string InputError => HasInputErrors ? "Enter brightness, contrast and vibrance from 0 to 100; gamma from 0.4 to 2.8." : "";
    public string BrightnessInput { get => ReadInput(nameof(BrightnessInput), Brightness * 100); set => SetInput(nameof(BrightnessInput), value, 0, 100, number => Brightness = number / 100); }
    public string ContrastInput { get => ReadInput(nameof(ContrastInput), Contrast * 100); set => SetInput(nameof(ContrastInput), value, 0, 100, number => Contrast = number / 100); }
    public string VibranceInput { get => ReadInput(nameof(VibranceInput), DigitalVibrance * 100); set => SetInput(nameof(VibranceInput), value, 0, 100, number => DigitalVibrance = number / 100); }
    public string GammaInput { get => ReadInput(nameof(GammaInput), Gamma); set => SetInput(nameof(GammaInput), value, .4, 2.8, number => Gamma = number); }
    private string ReadInput(string key, double number) => _invalidInputs.TryGetValue(key, out var raw) ? raw : number.ToString("0.##", CultureInfo.CurrentCulture);
    private void SetInput(string key, string raw, double minimum, double maximum, Action<double> assign)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && double.IsFinite(value) && value >= minimum && value <= maximum)
        { _invalidInputs.Remove(key); assign(value); }
        else _invalidInputs[key] = raw;
        NotifyOfPropertyChange(key); Publish();
    }
    public bool TrySetExactColorValue(string channel, string text)
    {
        var gamma = channel == nameof(Gamma);
        text = text.Trim();
        if (!gamma && text.EndsWith('%')) text = text[..^1].TrimEnd();
        if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
             !double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) ||
            !double.IsFinite(value) || value < (gamma ? .4 : 0) || value > (gamma ? 2.8 : 100))
            return false;
        switch (channel)
        {
            case nameof(Brightness): Brightness = value / 100; break;
            case nameof(Contrast): Contrast = value / 100; break;
            case nameof(DigitalVibrance): DigitalVibrance = value / 100; break;
            case nameof(Gamma): Gamma = value; break;
            default: return false;
        }
        return true;
    }

    private ProfileSetting SnapshotColors() => ProfileSetting.Copy();
    private void TrackColorEdit()
    {
        if (_resetting) return;
        if (_undoColors.Count >= 100)
        {
            var recent = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Reverse(System.Linq.Enumerable.Take(_undoColors, 99)));
            _undoColors.Clear(); foreach (var item in recent) _undoColors.Push(item);
        }
        _undoColors.Push(SnapshotColors());
        _redoColors.Clear();
        NotifyOfPropertyChange(nameof(CanUndoColor)); NotifyOfPropertyChange(nameof(CanRedoColor));
    }
    public void UndoColor()
    {
        if (!CanUndoColor) return;
        _redoColors.Push(SnapshotColors()); ApplyColorSnapshot(_undoColors.Pop());
    }
    public void RedoColor()
    {
        if (!CanRedoColor) return;
        _undoColors.Push(SnapshotColors()); ApplyColorSnapshot(_redoColors.Pop());
    }
    public void ApplyColorSnapshot(ProfileSetting snapshot)
    {
        _resetting = true;
        try { Brightness = snapshot.Brightness; Contrast = snapshot.Contrast; Gamma = snapshot.Gamma; DigitalVibrance = snapshot.DigitalVibrance; _invalidInputs.Clear(); }
        finally { _resetting = false; }
        Publish();
    }
    public void ResetBrightness() => Brightness = .5;
    public void ResetContrast() => Contrast = .5;
    public void ResetVibrance() => DigitalVibrance = .5;
    public void ResetGamma() => Gamma = 1;
    private void NotifyColorInputs()
    {
        foreach (var property in new[] { nameof(BrightnessInput), nameof(ContrastInput), nameof(VibranceInput), nameof(GammaInput), nameof(CanUndoColor), nameof(CanRedoColor), nameof(HasInputErrors), nameof(InputError) })
            NotifyOfPropertyChange(property);
    }
}
