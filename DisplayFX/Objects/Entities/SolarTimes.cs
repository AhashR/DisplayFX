using System;
namespace DisplayFX.Objects.Entities;

public static class SolarTimes
{
    // NOAA fractional-year approximation, zenith 90.833 degrees. Polar day/night has no event.
    public static DateTime? Calculate(DateTime date, double latitude, double longitude, bool sunrise, TimeZoneInfo zone)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180) return null;
        var year = 2 * Math.PI / (DateTime.IsLeapYear(date.Year) ? 366 : 365) * (date.DayOfYear - 1);
        var equation = 229.18 * (.000075 + .001868 * Math.Cos(year) - .032077 * Math.Sin(year) - .014615 * Math.Cos(2 * year) - .040849 * Math.Sin(2 * year));
        var declination = .006918 - .399912 * Math.Cos(year) + .070257 * Math.Sin(year) - .006758 * Math.Cos(2 * year) + .000907 * Math.Sin(2 * year) - .002697 * Math.Cos(3 * year) + .00148 * Math.Sin(3 * year);
        var lat = latitude * Math.PI / 180;
        var cosine = Math.Cos(90.833 * Math.PI / 180) / (Math.Cos(lat) * Math.Cos(declination)) - Math.Tan(lat) * Math.Tan(declination);
        if (cosine is < -1 or > 1 || !double.IsFinite(cosine)) return null;
        var angle = Math.Acos(cosine) * 180 / Math.PI * (sunrise ? 1 : -1);
        var utc = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc).AddMinutes(720 - 4 * (longitude + angle) - equation);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
    }
}
