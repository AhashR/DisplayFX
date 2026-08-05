using DisplayFX.Interface.ProfileSettings;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Objects.Factories.Interfaces;

public interface IProfileSettingViewModelFactory : IFactory
{
    ProfileSettingViewModel Create(ProfileSetting profileSetting, bool isDefault, Profile profile);
}