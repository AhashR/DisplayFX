using System.Dynamic;
using System.Windows;
using Caliburn.Micro;
using FluentResults;
using DisplayFX.Interface.Help;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.Factories.Interfaces;

namespace DisplayFX.Global;

public class DisplayWindowManager
{
    private readonly IHelpViewModelFactory _helpViewModelFactory;
    private readonly IProfileNameViewModelFactory _profileNameViewModelFactory;
    private readonly ISettingsViewModelFactory _settingsViewModelFactory;
    private readonly IWindowManager _windowManager;

    public DisplayWindowManager(
        IWindowManager windowManager,
        IHelpViewModelFactory helpViewModelFactory,
        IProfileNameViewModelFactory profileNameViewModelFactory,
        ISettingsViewModelFactory settingsViewModelFactory)
    {
        _windowManager = windowManager;
        _helpViewModelFactory = helpViewModelFactory;
        _profileNameViewModelFactory = profileNameViewModelFactory;
        _settingsViewModelFactory = settingsViewModelFactory;
    }

    public void OpenHelp()
    {
        var viewModel = _helpViewModelFactory.Create();
        _windowManager.ShowDialogAsync(viewModel);
    }

    public void OpenWebsite(string urlString)
    {
        WebsiteLauncher.OpenWebsite(urlString);
    }

    public Result<string> OpenProfileNameViewModel()
    {
        var viewModel = _profileNameViewModelFactory.Create();
        dynamic settings = new ExpandoObject();
        settings.Title = "New profile";
        settings.SizeToContent = SizeToContent.WidthAndHeight;
        settings.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        settings.ResizeMode = ResizeMode.NoResize;
        settings.GlowBrush = null;

        var result = _windowManager.ShowDialogAsync(viewModel, null, settings);
        return result.Result is true ? Result.Ok(viewModel.ProfileName) : Result.Fail("");
    }

    public void OpenSettings(Computer computer)
    {
        var viewModel = _settingsViewModelFactory.Create(computer);
        dynamic settings = new ExpandoObject();
        settings.Title = "App settings";
        settings.SizeToContent = SizeToContent.WidthAndHeight;
        settings.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        settings.ResizeMode = ResizeMode.NoResize;
        settings.GlowBrush = null;

        _windowManager.ShowDialogAsync(viewModel, null, settings);
    }

    public void ShowMessageBox(string message)
    {
        MessageBox.Show(message);
    }
}