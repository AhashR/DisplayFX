using System.Collections.Generic;
using System.Drawing;
using Newtonsoft.Json;

namespace DisplayFX.Objects.Entities;

public class Monitor
{
    public Monitor(string displayDevicePath, string name, Size resolution, int frequency)
    {
        DisplayDevicePath = displayDevicePath;
        Name = name;
        Resolution = resolution;
        Frequency = frequency;
    }

    public string DisplayDevicePath { get; }
    public string Name { get; private set; }
    public string? CustomName { get; set; }
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(CustomName) ? Name : CustomName.Trim();
    public Size Resolution { get; private set; }
    public int Frequency { get; private set; }
    public int? LastBrightness { get; set; }
    public List<Profile> Profiles { get; set; } = new();

    internal bool RefreshMetadata(Monitor detected)
    {
        var changed = Name != detected.Name || Resolution != detected.Resolution || Frequency != detected.Frequency;
        Name = detected.Name;
        Resolution = detected.Resolution;
        Frequency = detected.Frequency;
        return changed;
    }
}
