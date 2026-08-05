using DisplayFX.Interface.ProfileNames;
using DisplayFX.Objects.Factories.Interfaces;

namespace DisplayFX.Objects.Factories;

public class ProfileNameViewModelFactory : IProfileNameViewModelFactory
{
    public ProfileNameViewModel Create()
    {
        return new ProfileNameViewModel();
    }
}
