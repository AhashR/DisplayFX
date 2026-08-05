using DisplayFX.Interface.Monitors;
using DisplayFX.Interface.Profiles;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Objects.Factories.Interfaces;

public interface IProfileViewModelFactory : IFactory
{
    ProfileViewModel Create(Profile profile, MonitorViewModel monitorViewModel);
}