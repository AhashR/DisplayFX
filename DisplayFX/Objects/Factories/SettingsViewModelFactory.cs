using DisplayFX.Global.Controllers;
using DisplayFX.Interface.Settings;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.Factories.Interfaces;

namespace DisplayFX.Objects.Factories;

public class SettingsViewModelFactory : ISettingsViewModelFactory
{
    private readonly RegistryController _registryController;
    private readonly DataController _dataController;
    private readonly BrightnessAutomationController _automation;

    public SettingsViewModelFactory(RegistryController registryController, DataController dataController,
        BrightnessAutomationController automation)
    {
        _registryController = registryController;
        _dataController = dataController;
        _automation = automation;
    }

    public SettingsViewModel Create(Computer computer)
    {
        return new SettingsViewModel(computer, _registryController, _dataController, _automation);
    }
}
