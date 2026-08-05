using DisplayFX.Interface.Settings;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Objects.Factories.Interfaces;

public interface ISettingsViewModelFactory : IFactory
{
    SettingsViewModel Create(Computer computer);
}
