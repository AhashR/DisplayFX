using DisplayFX.Interface.Monitors;
using DisplayFX.Interface.Profiles;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.Factories.Interfaces;

namespace DisplayFX.Objects.Factories;

public class ProfileViewModelFactory : IProfileViewModelFactory
{
    private readonly IProfileSettingViewModelFactory _profileSettingViewModelFactory;

    public ProfileViewModelFactory(IProfileSettingViewModelFactory profileSettingViewModelFactory)
    {
        _profileSettingViewModelFactory = profileSettingViewModelFactory;
    }

    public ProfileViewModel Create(Profile profile, MonitorViewModel monitorViewModel)
    {
        return new ProfileViewModel(profile, monitorViewModel, _profileSettingViewModelFactory);
    }
}
