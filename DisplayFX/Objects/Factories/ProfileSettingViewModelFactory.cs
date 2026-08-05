using Caliburn.Micro;
using DisplayFX.Interface.ProfileSettings;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.Factories.Interfaces;

namespace DisplayFX.Objects.Factories;

public class ProfileSettingViewModelFactory : IProfileSettingViewModelFactory
{
    private readonly IEventAggregator _eventAggregator;

    public ProfileSettingViewModelFactory(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
    }

    public ProfileSettingViewModel Create(ProfileSetting profileSetting, bool isDefault, Profile profile)
    {
        return new ProfileSettingViewModel(profileSetting, isDefault, _eventAggregator, profile);
    }
}
