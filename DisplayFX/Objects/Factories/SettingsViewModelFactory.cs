using System;
using System.Threading.Tasks;
using System.Windows;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.Settings;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.Factories.Interfaces;

namespace DisplayFX.Objects.Factories;

public class SettingsViewModelFactory : ISettingsViewModelFactory
{
    private readonly RegistryController _registryController;
    private readonly DataController _dataController;

    public SettingsViewModelFactory(RegistryController registryController, DataController dataController)
    {
        _registryController = registryController;
        _dataController = dataController;
    }

    public SettingsViewModel Create(Computer computer, Func<Window?, string?> import, Func<Window?, string?> export, Func<Task<string>> checkUpdates, Func<bool> canImport, Func<bool> canExport, Func<string> profileFileContext)
    {
        return new SettingsViewModel(computer, _registryController, _dataController, import, export, checkUpdates, canImport, canExport, profileFileContext);
    }
}
