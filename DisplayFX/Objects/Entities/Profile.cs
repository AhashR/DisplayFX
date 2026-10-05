using System.Windows.Input;
using Newtonsoft.Json;

namespace DisplayFX.Objects.Entities;

public class Profile
{
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

    // Hotkey properties
    public ModifierKeys? HotkeyModifiers { get; set; }
    public Key? HotkeyKey { get; set; }
}
