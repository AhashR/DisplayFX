using DisplayFX.Global.Controllers;
using DisplayFX.Interface.Help;
using DisplayFX.Objects.Factories.Interfaces;

namespace DisplayFX.Objects.Factories;

public class HelpViewModelFactory : IHelpViewModelFactory
{
    private readonly DataController _dataController;
    private readonly ComputerFactory _computerFactory;

    public HelpViewModelFactory(DataController dataController, ComputerFactory computerFactory)
    {
        _dataController = dataController;
        _computerFactory = computerFactory;
    }

    public HelpViewModel Create()
    {
        return new HelpViewModel(_dataController, _computerFactory);
    }
}
