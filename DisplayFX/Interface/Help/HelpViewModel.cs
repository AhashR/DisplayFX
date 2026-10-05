using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using DisplayFX.Bootstrap;
using DisplayFX.Global;
using DisplayFX.Global.Controllers;
using DisplayFX.Global.Extensions;
using DisplayFX.Objects.Factories;
using DisplayFX.Objects.Entities;
using NLog;
using Screen = Caliburn.Micro.Screen;

namespace DisplayFX.Interface.Help;

public class HelpViewModel : Screen
{
    public string VersionText => $"Build: {DisplayFX.Global.AppVersion.Current}";
    private readonly ComputerFactory _computerFactory;
    private readonly DataController _dataController;
    private readonly RegistryController _registryController;
    private readonly BrightnessPersistenceController _brightnessPersistenceController;
    private readonly ILogger _logger;
    private readonly BrightnessAutomationController? _automation;

    public HelpViewModel(DataController dataController, ComputerFactory computerFactory,
        RegistryController registryController, BrightnessPersistenceController brightnessPersistenceController,
        ILogger logger, BrightnessAutomationController? automation = null)
    {
        _dataController = dataController;
        _computerFactory = computerFactory;
        _registryController = registryController;
        _brightnessPersistenceController = brightnessPersistenceController;
        _logger = logger;
        _automation = automation;

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
        try
        {
            var original = _dataController.Load();
            if (original.IsFailed)
                throw new InvalidOperationException("Current settings could not be loaded, so they have been preserved. " +
                    string.Join("; ", original.Errors.Select(error => error.Message)));
            var defaults = _computerFactory.Create();
            if (defaults.IsFailed)
                throw new InvalidOperationException(string.Join("; ", defaults.Errors.Select(error => error.Message)));

            ResetAndRestart(original.Value, defaults.Value, _dataController.Write,
                _registryController.RegisterForStartWithWindows,
                () => Bootstrapper.RestartApplication(() =>
                {
                    _automation?.Detach();
                    _brightnessPersistenceController.Detach();
                }));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to reset DisplayFX settings.");
            MessageBox.Show("DisplayFX could not reset and restart. " + exception.Message,
                "Reset failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    internal static void ResetAndRestart(Computer original, Computer defaults, Action<Computer> write,
        Action<bool> setStartWithWindows, Action restart)
    {
        var wroteDefaults = false;
        var startupUpdateAttempted = false;
        try
        {
            write(defaults);
            wroteDefaults = true;
            startupUpdateAttempted = true;
            setStartWithWindows(defaults.IsStartWithWindows);
            restart();
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            if (wroteDefaults)
            {
                try { write(original); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            if (startupUpdateAttempted)
            {
                try { setStartWithWindows(original.IsStartWithWindows); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            if (failures.Count > 1)
                throw new AggregateException("Reset failed and the original settings could not be fully restored. " +
                    string.Join("; ", failures.Select(exception => exception.Message)), failures);
            throw;
        }
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

    public event System.EventHandler? CanExecuteChanged { add { } remove { } }
}
