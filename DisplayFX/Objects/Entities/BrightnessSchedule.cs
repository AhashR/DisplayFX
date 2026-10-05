using System;
using System.Globalization;
using Newtonsoft.Json;

namespace DisplayFX.Objects.Entities;

/// <summary>A daily brightness change for all connected monitors with DDC/CI support.</summary>
public class BrightnessSchedule
{
    public string Time { get; set; } = "20:00";
    public int Brightness { get; set; } = 40;

    [JsonIgnore]
    public bool IsValid => TryGetTime(out _) && Brightness is >= 0 and <= 100;

    public bool TryGetTime(out TimeSpan time) => TimeSpan.TryParseExact(Time,
        @"hh\:mm", CultureInfo.InvariantCulture, out time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);
}
