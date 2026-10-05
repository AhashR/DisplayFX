using System;
using System.Dynamic;
using System.Collections.Generic;
using System.Threading.Tasks;
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

    public async Task<Result<string>> OpenProfileNameViewModel(string title = "New profile",
        string initialName = "", IEnumerable<string>? existingNames = null)
    {
        var viewModel = _profileNameViewModelFactory.Create();
        viewModel.Configure(title, initialName, existingNames);
        dynamic settings = new ExpandoObject();
        settings.Title = title;
        settings.SizeToContent = SizeToContent.WidthAndHeight;
        settings.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        settings.ResizeMode = ResizeMode.NoResize;
        settings.GlowBrush = null;

        var result = await _windowManager.ShowDialogAsync(viewModel, null, settings);
        return result is true ? Result.Ok(viewModel.ProfileName) : Result.Fail("");
    }

    public async Task<string?> OpenSettings(Computer computer)
    {
        var viewModel = _settingsViewModelFactory.Create(computer);
        dynamic settings = new ExpandoObject();
        settings.Title = "Settings";
        settings.MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 32);
        settings.SizeToContent = SizeToContent.WidthAndHeight;
        settings.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        settings.ResizeMode = ResizeMode.CanMinimize;

        await _windowManager.ShowDialogAsync(viewModel, null, settings);
        return viewModel.RestoreFilePath;
    }

    public void ShowMessageBox(string message)
    {
        MessageBox.Show(message);
    }
}
