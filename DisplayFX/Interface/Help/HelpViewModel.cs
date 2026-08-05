using System.Windows.Forms;
using System.Windows.Input;
using DisplayFX.Global;
using DisplayFX.Global.Controllers;
using DisplayFX.Global.Extensions;
using DisplayFX.Objects.Factories;
using Screen = Caliburn.Micro.Screen;

namespace DisplayFX.Interface.Help;

public class HelpViewModel : Screen
{
    private readonly ComputerFactory _computerFactory;
    private readonly DataController _dataController;

    public HelpViewModel(DataController dataController, ComputerFactory computerFactory)
    {
        _dataController = dataController;
        _computerFactory = computerFactory;

        OpenWebsiteCommand = new RelayCommand<object>(MyAction);
    }

    public ICommand OpenWebsiteCommand { get; }

    public override string DisplayName
    {
        get => "About";
        set { }
    }

    private void MyAction(object website)
    {
        if (website is string websiteValue)
            WebsiteLauncher.OpenWebsite(websiteValue);
    }

    public void Reset()
    {
        _computerFactory.Create()
            .IfSuccess(computer =>
            {
                _dataController.Write(computer);

                Application.Restart();
                System.Windows.Application.Current.Shutdown();
            });
    }
}

public sealed class RelayCommand<T> : ICommand
{
    private readonly System.Action<T> _execute;

    public RelayCommand(System.Action<T> execute)
    {
        _execute = execute;
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        if (parameter is T typedParameter)
            _execute(typedParameter);
    }

    public event System.EventHandler? CanExecuteChanged;
}