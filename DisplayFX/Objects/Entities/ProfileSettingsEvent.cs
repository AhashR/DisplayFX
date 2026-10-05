namespace DisplayFX.Objects.Entities;

public class ProfileSettingsEvent
{
    public ProfileSettingsEvent(Profile profile, bool isDirty)
    {
        Profile = profile;
        IsDirty = isDirty;
    }

    public Profile Profile { get; }
    public bool IsDirty { get; }
}
