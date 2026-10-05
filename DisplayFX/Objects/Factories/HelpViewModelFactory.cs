using DisplayFX.Global.Controllers;
using DisplayFX.Interface.Help;
using DisplayFX.Objects.Factories.Interfaces;
using NLog;

namespace DisplayFX.Objects.Factories;

public class HelpViewModelFactory : IHelpViewModelFactory
{
    private readonly DataController _dataController;
    private readonly ComputerFactory _computerFactory;
    private readonly RegistryController _registryController;
    private readonly BrightnessPersistenceController _brightnessPersistenceController;
    private readonly ILogger _logger;
    private readonly BrightnessAutomationController _automation;

    public HelpViewModelFactory(DataController dataController, ComputerFactory computerFactory,
        RegistryController registryController, BrightnessPersistenceController brightnessPersistenceController,
        ILogger logger, BrightnessAutomationController automation)
    {
        _dataController = dataController;
        _computerFactory = computerFactory;
        _registryController = registryController;
        _brightnessPersistenceController = brightnessPersistenceController;
        _logger = logger;
        _automation = automation;
    }

    public HelpViewModel Create()
    {
        return new HelpViewModel(_dataController, _computerFactory, _registryController,
            _brightnessPersistenceController, _logger, _automation);
    }
}
