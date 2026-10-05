using DisplayFX.Objects.Entities;

namespace DisplayFX.Objects.Factories;

public class ProfileFactory
{
    public Profile CreateDefault(Monitor monitor)
    {
        return new Profile(monitor, "Default",
            new ProfileSetting(0.5, 0.5, 1.0, 0.5), true, true);
    }

    public Profile Create(Monitor monitor, string name)
    {
        return new Profile(monitor, name, new ProfileSetting(0.5, 0.5, 1.0, 0.5), false);
    }
}
