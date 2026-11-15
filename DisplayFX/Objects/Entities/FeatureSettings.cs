using System;
using System.Collections.Generic;
using System.Linq;

namespace DisplayFX.Objects.Entities;

public sealed class FeatureSettings
{
    public string Theme { get; set; } = "System";
    public string Accent { get; set; } = "#80B5FA";
    public string Density { get; set; } = "Comfortable";
    public string Language { get; set; } = "en";
    public int TextScalePercent { get; set; } = 100;
    public bool ShowActionLabels { get; set; } = true;
    public bool ReduceMotion { get; set; }
    public bool OnboardingCompleted { get; set; }
    public bool AutomaticSwitchingEnabled { get; set; } = true;
    public int FocusDelayMilliseconds { get; set; } = 750;
    public int StartupDelaySeconds { get; set; }
    public bool ReapplyAfterWake { get; set; } = true;
    public bool ReapplyAfterDock { get; set; } = true;
    public bool ShowNotifications { get; set; }
    public string BrightnessTarget { get; set; } = "All";
    public string? TargetDevicePath { get; set; }
    public bool PinBrightnessPopup { get; set; }
    public string PopupOrder { get; set; } = "Favorites";
    public bool ShowHardwareDetails { get; set; }
    public bool VersionedBackups { get; set; } = true;
    public int BackupRetention { get; set; } = 10;
    public string UpdateChannel { get; set; } = "Stable";
    public bool DetectExternalColors { get; set; }
    public int TransitionMilliseconds { get; set; }
    public int ColorTransitionMilliseconds { get; set; }
    public double? SolarLatitude { get; set; }
    public double? SolarLongitude { get; set; }
    public List<ProfileSet> ProfileSets { get; set; } = new();
    public List<CompatibilityRecord> Compatibility { get; set; } = new();

    public string? Validate()
    {
        if (Theme is not ("System" or "Dark" or "Light") || Density is not ("Compact" or "Comfortable") ||
            Language is not ("en" or "nl") || UpdateChannel is not ("Stable" or "Test") ||
            BrightnessTarget is not ("All" or "Selected" or "Pointer") || PopupOrder is not ("Saved" or "Name" or "Favorites")) return "Invalid application preference.";
        if (Accent == null || !System.Text.RegularExpressions.Regex.IsMatch(Accent, "^#[0-9a-fA-F]{6}$")) return "Accent must be #RRGGBB.";
        if (FocusDelayMilliseconds is < 0 or > 10000 || StartupDelaySeconds is < 0 or > 120 ||
            TransitionMilliseconds is < 0 or > 30000 || ColorTransitionMilliseconds is < 0 or > 5000 || BackupRetention is < 1 or > 100 || TextScalePercent is < 100 or > 200) return "Preference is outside its supported range.";
        if (SolarLatitude is double lat && (!double.IsFinite(lat) || lat is < -90 or > 90) ||
            SolarLongitude is double lon && (!double.IsFinite(lon) || lon is < -180 or > 180) || SolarLatitude.HasValue != SolarLongitude.HasValue)
            return "Enter both latitude (−90…90) and longitude (−180…180).";
        if (ProfileSets == null || ProfileSets.Count > 100 || Compatibility == null || Compatibility.Count > 256) return "Too many saved sets or compatibility records.";
        foreach (var set in ProfileSets)
            if (set == null || string.IsNullOrWhiteSpace(set.Name) || set.Name.Length > 80 || set.Entries == null || set.Entries.Count is < 1 or > 64 ||
                set.Entries.Exists(entry => entry == null || string.IsNullOrWhiteSpace(entry.DevicePath) || entry.DevicePath.Length > 2048 || entry.ProfileId == Guid.Empty) ||
                System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Select(set.Entries, entry => entry.DevicePath), StringComparer.OrdinalIgnoreCase).Count() != set.Entries.Count)
                return "Invalid multi-monitor profile set.";
        foreach (var record in Compatibility)
            if (record == null || string.IsNullOrWhiteSpace(record.DevicePath) || record.DevicePath.Length > 2048 || record.Notes == null || record.Notes.Length > 4000)
                return "Invalid compatibility record.";
        return null;
    }
}

public sealed class ProfileSet
{
    public string Name { get; set; } = "";
    public List<ProfileSetEntry> Entries { get; set; } = new();
}
public sealed class ProfileSetEntry
{
    public string DevicePath { get; set; } = "";
    public Guid ProfileId { get; set; }
}
public sealed class CompatibilityRecord
{
    public string DevicePath { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;
}
