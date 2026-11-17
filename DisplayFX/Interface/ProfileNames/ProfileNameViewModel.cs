using System;
using System.Collections.Generic;
using System.Linq;
using Caliburn.Micro;

namespace DisplayFX.Interface.ProfileNames;

public class ProfileNameViewModel : Screen
{
    private string _profileName = string.Empty;
    private string _title = "New profile";
    private HashSet<string> _existingNames = new(StringComparer.OrdinalIgnoreCase);

    public override string DisplayName
    {
        get => _title;
        set { }
    }

    public string ProfileName
    {
        get => _profileName;
        set
        {
            if (value == _profileName) return;
            _profileName = value;
            NotifyOfPropertyChange();
            NotifyOfPropertyChange(nameof(CanSave));
            NotifyOfPropertyChange(nameof(ValidationMessage));
        }
    }

    public string ValidationMessage => string.IsNullOrWhiteSpace(ProfileName)
        ? "Enter a profile name."
        : ProfileName.Trim().Length > 80
            ? "Use 80 characters or fewer."
            : ProfileName.Any(char.IsControl)
                ? "The name cannot contain control characters."
                : _existingNames.Contains(ProfileName.Trim())
                    ? "A profile with this name already exists on this monitor."
                    : string.Empty;

    public bool CanSave => ValidationMessage.Length == 0;

    public void Configure(string title, string initialName, IEnumerable<string>? existingNames = null)
    {
        _title = title;
        _existingNames = new HashSet<string>(existingNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        ProfileName = initialName;
        NotifyOfPropertyChange(nameof(DisplayName));
        NotifyOfPropertyChange(nameof(ValidationMessage));
        NotifyOfPropertyChange(nameof(CanSave));
    }

    public void Save()
    {
        if (!CanSave) return;
        ProfileName = ProfileName.Trim();
        TryCloseAsync(true);
    }

    public void Cancel()
    {
        TryCloseAsync(false);
    }
}
