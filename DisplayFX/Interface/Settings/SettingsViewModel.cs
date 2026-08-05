using Caliburn.Micro;
using DisplayFX.Global.Controllers;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Interface.Settings;

public class SettingsViewModel : Screen
{
    private readonly Computer _computer;
    private readonly RegistryController _registryController;

    public SettingsViewModel(Computer computer, RegistryController registryController)
    {
        _computer = computer;
        _registryController = registryController;
    }

    public bool IsStartWithWindows
    {
        get => _computer.IsStartWithWindows;
        set
        {
            if (value == _computer.IsStartWithWindows) return;
            _computer.IsStartWithWindows = value;
            _registryController.RegisterForStartWithWindows(value);
            NotifyOfPropertyChange();
        }
    }

    public bool IsStartMinimized
    {
        get => _computer.IsStartMinimized;
        set
        {
            if (value == _computer.IsStartMinimized) return;
            _computer.IsStartMinimized = value;
            NotifyOfPropertyChange();
        }
    }

    public bool IsMinimizeToTray
    {
        get => _computer.IsMinimizeToTray;
        set
        {
            if (value == _computer.IsMinimizeToTray) return;
            _computer.IsMinimizeToTray = value;
            NotifyOfPropertyChange();
        }
    }

    public bool IsApplySettingsOnStart
    {
        get => _computer.IsApplySettingsOnStart;
        set
        {
            if (value == _computer.IsApplySettingsOnStart) return;
            _computer.IsApplySettingsOnStart = value;
            NotifyOfPropertyChange();
        }
    }

    public void Close()
    {
        TryCloseAsync(true);
    }
}
