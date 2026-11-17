using System;
using System.Collections.Generic;
using System.Linq;

namespace DisplayFX.Objects.Entities;

public sealed class FeatureSettings
{
    // Preserve previously saved data for compatibility. Profile sets are no longer exposed.
    public List<ProfileSet> ProfileSets { get; set; } = new();

    public string? Validate()
    {
        if (ProfileSets == null || ProfileSets.Count > 100) return "Too many saved profile sets.";
        foreach (var set in ProfileSets)
            if (set == null || string.IsNullOrWhiteSpace(set.Name) || set.Name.Length > 80 || set.Entries == null || set.Entries.Count is < 1 or > 64 ||
                set.Entries.Exists(entry => entry == null || string.IsNullOrWhiteSpace(entry.DevicePath) || entry.DevicePath.Length > 2048 || entry.ProfileId == Guid.Empty) ||
                System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Select(set.Entries, entry => entry.DevicePath), StringComparer.OrdinalIgnoreCase).Count() != set.Entries.Count)
                return "Invalid multi-monitor profile set.";
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
