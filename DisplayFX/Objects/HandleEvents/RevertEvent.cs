using DisplayFX.Objects.Entities;

namespace DisplayFX.Objects.HandleEvents;

public class RevertEvent
{
    public RevertEvent(Profile profile)
    {
        Profile = profile;
    }

    public Profile Profile { get; }
}
