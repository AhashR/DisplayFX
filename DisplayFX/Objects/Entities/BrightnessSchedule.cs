using System;
using System.Globalization;
using Newtonsoft.Json;

namespace DisplayFX.Objects.Entities;

/// <summary>A daily brightness change for all connected monitors with DDC/CI support.</summary>
public class BrightnessSchedule
{
    public string Time { get; set; } = "20:00";
    private int _brightness = 40;
    private string? _brightnessInput;
    public int Brightness { get => _brightness; set { _brightness = value; _brightnessInput = null; } }
    [JsonIgnore]
    public string BrightnessInput
    {
        get => _brightnessInput ?? Brightness.ToString(CultureInfo.InvariantCulture);
        set
        {
            _brightnessInput = value;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)) _brightness = level;
        }
    }

    [JsonIgnore]
    public bool IsValid => TryGetTime(out _) && Brightness is >= 0 and <= 100 &&
        (_brightnessInput == null || int.TryParse(_brightnessInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out _));

    public bool TryGetTime(out TimeSpan time) => TimeSpan.TryParseExact(Time,
        @"hh\:mm", CultureInfo.InvariantCulture, out time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);
}
