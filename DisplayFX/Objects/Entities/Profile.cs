using System.Windows.Input;
using Newtonsoft.Json;

namespace DisplayFX.Objects.Entities;

public class Profile
{
    public System.Guid Id { get; set; } = System.Guid.NewGuid();
    public bool IsFavorite { get; set; }
    public string Icon { get; set; } = "●";
    public string MarkerColor { get; set; } = "#80B5FA";
    public string Notes { get; set; } = "";
    public System.DateTimeOffset ModifiedAt { get; set; } = System.DateTimeOffset.UtcNow;
    public System.Collections.Generic.List<string> AdditionalExecutablePaths { get; set; } = new();
    public int RulePriority { get; set; }
    public bool FullscreenOnly { get; set; }
    public string PowerCondition { get; set; } = "Any";
    public Profile(Monitor monitor, string name, ProfileSetting profileSetting, bool isActive,
        bool isDefault = false)
    {
        Monitor = monitor;
        Name = name;
        ProfileSetting = profileSetting;
        IsActive = isActive;
        IsDefault = isDefault;
    }

    [JsonIgnore]
    public Monitor Monitor { get; internal set; }
    public string Name { get; set; }
    public ProfileSetting ProfileSetting { get; }
    public bool IsActive { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsActiveBeforeAutomaticSwitch { get; set; }
    public bool IsDefault { get; set; }
    public string? LinkedExecutablePath { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? ImportedProfileName { get; set; }

    // Hotkey properties
    public ModifierKeys? HotkeyModifiers { get; set; }
    public Key? HotkeyKey { get; set; }
}
