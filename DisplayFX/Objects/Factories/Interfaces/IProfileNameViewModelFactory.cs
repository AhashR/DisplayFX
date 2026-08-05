using DisplayFX.Interface.ProfileNames;

namespace DisplayFX.Objects.Factories.Interfaces;

public interface IProfileNameViewModelFactory : IFactory
{
    ProfileNameViewModel Create();
}