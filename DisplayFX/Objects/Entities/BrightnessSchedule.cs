using System;
using System.Globalization;
using Newtonsoft.Json;

namespace DisplayFX.Objects.Entities;

/// <summary>A daily brightness change for all connected monitors with DDC/CI support.</summary>
public class BrightnessSchedule
{
    public string Time { get; set; } = "20:00";
    public int Days { get; set; } = 127;
    [JsonIgnore] public bool Sunday { get => (Days & 1) != 0; set => SetDay(1, value); }
    [JsonIgnore] public bool Monday { get => (Days & 2) != 0; set => SetDay(2, value); }
    [JsonIgnore] public bool Tuesday { get => (Days & 4) != 0; set => SetDay(4, value); }
    [JsonIgnore] public bool Wednesday { get => (Days & 8) != 0; set => SetDay(8, value); }
    [JsonIgnore] public bool Thursday { get => (Days & 16) != 0; set => SetDay(16, value); }
    [JsonIgnore] public bool Friday { get => (Days & 32) != 0; set => SetDay(32, value); }
    [JsonIgnore] public bool Saturday { get => (Days & 64) != 0; set => SetDay(64, value); }
    private void SetDay(int mask, bool enabled) => Days = enabled ? Days | mask : Days & ~mask;
    public string? DevicePath { get; set; }
    public string SolarEvent { get; set; } = "Fixed";
    private int _offsetMinutes;
    private string? _offsetInput;
    public int OffsetMinutes { get => _offsetMinutes; set { _offsetMinutes = value; _offsetInput = null; } }
    [JsonIgnore] public string OffsetInput
    {
        get => _offsetInput ?? OffsetMinutes.ToString(CultureInfo.InvariantCulture);
        set { _offsetInput = value; if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset)) _offsetMinutes = offset; }
    }
    public BrightnessSchedule Copy() => new() { Time = Time, Brightness = Brightness, Days = Days, DevicePath = DevicePath, SolarEvent = SolarEvent, OffsetMinutes = OffsetMinutes };
    public DateTime? Occurrence(DateTime date, double? latitude, double? longitude)
    {
        DateTime? occurrence = SolarEvent == "Fixed" ? TryGetTime(out var time) ? date.Date + time : null :
            latitude.HasValue && longitude.HasValue ? SolarTimes.Calculate(date, latitude.Value, longitude.Value, SolarEvent == "Sunrise", TimeZoneInfo.Local) : null;
        if (!occurrence.HasValue || (Days & (1 << (int)occurrence.Value.DayOfWeek)) == 0) return null;
        return occurrence?.AddMinutes(OffsetMinutes);
    }
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
    public bool IsValid => (SolarEvent is "Sunrise" or "Sunset" || SolarEvent == "Fixed" && TryGetTime(out _)) && Days is >= 1 and <= 127 && OffsetMinutes is >= -720 and <= 720 &&
        (DevicePath == null || DevicePath.Length <= 2048 && !System.Linq.Enumerable.Any(DevicePath, char.IsControl)) && Brightness is >= 0 and <= 100 &&
        (_brightnessInput == null || int.TryParse(_brightnessInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) &&
        (_offsetInput == null || int.TryParse(_offsetInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out _));

    public bool TryGetTime(out TimeSpan time) => TimeSpan.TryParseExact(Time,
        @"hh\:mm", CultureInfo.InvariantCulture, out time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);
}
