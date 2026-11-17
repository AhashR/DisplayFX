using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Caliburn.Micro;
using DisplayFX.Global;
using System.Windows;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.ProfileSettings;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Interface.Settings;

public class SettingsViewModel : Screen
{
    private readonly Computer _computer;
    private readonly DataController _dataController;
    private readonly RegistryController _registryController;
    private string _settingsStatus = "";
    private bool _checkingUpdates;
    private readonly Func<Window?, string?> _import;
    private readonly Func<Window?, string?> _export;
    private readonly Func<Task<string>> _checkUpdates;
    private readonly Func<bool> _canImport;
    private readonly Func<bool> _canExport;
    private readonly Func<string> _profileFileContext;

    public SettingsViewModel(Computer computer, RegistryController registryController, DataController dataController,
        Func<Window?, string?>? import = null, Func<Window?, string?>? export = null, Func<Task<string>>? checkUpdates = null,
        Func<bool>? canImport = null, Func<bool>? canExport = null, Func<string>? profileFileContext = null)
    {
        _computer = computer;
        _registryController = registryController;
        _dataController = dataController;
        _import = import ?? (_ => null);
        _export = export ?? (_ => null);
        _checkUpdates = checkUpdates ?? (() => Task.FromResult(""));
        _canImport = canImport ?? (() => false);
        _canExport = canExport ?? (() => false);
        _profileFileContext = profileFileContext ?? (() => "No display selected.");
    }

    public string VersionText => $"Version {AppVersion.Current}";
    public string ProfileFileContext => _profileFileContext();
    public bool CanImportProfile => _canImport();
    public bool CanExportProfile => _canExport();
    public bool CanCheckForUpdates => !_checkingUpdates;
    public ICommand ImportProfileCommand => new RelayCommand(() => Transfer(_import));
    public ICommand ExportProfileCommand => new RelayCommand(() => Transfer(_export));

    private void Transfer(Func<Window?, string?> action)
    {
        string? result;
        try { result = action(GetView() as Window); }
        catch (Exception ex) { SettingsStatus = "Could not open the profile file dialog. " + ex.Message; return; }
        if (result == null) return;
        SettingsStatus = result;
        NotifyOfPropertyChange(nameof(ProfileFileContext));
        NotifyOfPropertyChange(nameof(CanImportProfile));
        NotifyOfPropertyChange(nameof(CanExportProfile));
    }

    public async Task CheckForUpdates()
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        NotifyOfPropertyChange(nameof(CanCheckForUpdates));
        SettingsStatus = "Checking for updates…";
        try { SettingsStatus = await _checkUpdates(); }
        catch (Exception ex) { SettingsStatus = "Could not check for updates. " + ex.Message; }
        finally { _checkingUpdates = false; NotifyOfPropertyChange(nameof(CanCheckForUpdates)); }
    }

    public bool IsStartWithWindows
    {
        get => _computer.IsStartWithWindows;
        set
        {
            if (value == _computer.IsStartWithWindows) return;
            SaveOption(() => _computer.IsStartWithWindows, newValue => _computer.IsStartWithWindows = newValue,
                value, nameof(IsStartWithWindows), true);
        }
    }

    public bool IsStartMinimized
    {
        get => _computer.IsStartMinimized;
        set
        {
            if (value == _computer.IsStartMinimized) return;
            SaveOption(() => _computer.IsStartMinimized, newValue => _computer.IsStartMinimized = newValue,
                value, nameof(IsStartMinimized));
        }
    }

    public bool IsMinimizeToTray
    {
        get => _computer.IsMinimizeToTray;
        set
        {
            if (value == _computer.IsMinimizeToTray) return;
            SaveOption(() => _computer.IsMinimizeToTray, newValue => _computer.IsMinimizeToTray = newValue,
                value, nameof(IsMinimizeToTray));
        }
    }

    public string SettingsStatus
    {
        get => _settingsStatus;
        private set { _settingsStatus = value; NotifyOfPropertyChange(); }
    }

    private void SaveOption(Func<bool> get, Action<bool> set, bool value, string property, bool startup = false)
    {
        var previous = get();
        try
        {
            if (startup) _registryController.RegisterForStartWithWindows(value);
            set(value);
            _dataController.Write(_computer);
            SettingsStatus = "";
        }
        catch (Exception ex)
        {
            set(previous);
            if (startup)
            {
                try { _registryController.RegisterForStartWithWindows(previous); }
                catch (Exception rollback) { SettingsStatus = "Startup preference could not be restored. " + rollback.Message; NotifyOfPropertyChange(property); return; }
            }
            SettingsStatus = "Could not save setting. " + ex.Message;
        }
        NotifyOfPropertyChange(property);
    }
}
