using DisplayFX.Global.Controllers;
using DisplayFX.Interface.Settings;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.Factories.Interfaces;

namespace DisplayFX.Objects.Factories;

public class SettingsViewModelFactory : ISettingsViewModelFactory
{
    private readonly RegistryController _registryController;

    public SettingsViewModelFactory(RegistryController registryController)
    {
        _registryController = registryController;
    }

    public SettingsViewModel Create(Computer computer)
    {
        return new SettingsViewModel(computer, _registryController);
    }
}
