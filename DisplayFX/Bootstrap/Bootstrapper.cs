using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Caliburn.Micro;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NvAPIWrapper;
using DisplayFX.Global;
using DisplayFX.Global.Controllers;
using DisplayFX.Global.Extensions;
using DisplayFX.Interface.Shell;
using DisplayFX.Objects.Factories;
using DisplayFX.Objects.Factories.Interfaces;

namespace DisplayFX.Bootstrap;

public class Bootstrapper : BootstrapperBase
{
    private readonly ServiceProvider _serviceProvider;

    // Used for Window Management
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    // Used for cross process communication 
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    // custom message
    private const int WM_SHOWME = 0x0400 + 1; // WM_USER + 1
    
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_RESTORE = 9;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    public Bootstrapper()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID("DisplayFX.App");
        }
        catch { }

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        Initialize();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWindowManager, WindowManager>();
        services.AddSingleton<IEventAggregator, EventAggregator>();
        services.AddSingleton<ILogger>(_ => NLog.LogManager.GetCurrentClassLogger());

        services.AddSingleton<DisplayCache>();
        services.AddSingleton<ProcessController>();
        services.AddSingleton<RegistryController>();
        services.AddSingleton<DisplayController>();
        services.AddSingleton<DataController>();

        services.AddTransient<ProfileFactory>();
        services.AddTransient<MonitorFactory>();
        services.AddTransient<ComputerFactory>();
        services.AddTransient<MonitorViewModelFactory>();

        services.AddTransient<IProfileViewModelFactory, ProfileViewModelFactory>();
        services.AddTransient<IProfileSettingViewModelFactory, ProfileSettingViewModelFactory>();
        services.AddTransient<IHelpViewModelFactory, HelpViewModelFactory>();
        services.AddTransient<IProfileNameViewModelFactory, ProfileNameViewModelFactory>();
        services.AddTransient<ISettingsViewModelFactory, SettingsViewModelFactory>();

        services.AddTransient<DisplayWindowManager>();
        services.AddTransient<ShellViewModel>();
    }

    private ComputerFactory _computerFactory => _serviceProvider.GetRequiredService<ComputerFactory>();
    private DataController _dataController => _serviceProvider.GetRequiredService<DataController>();
    private ILogger _fileLogger => _serviceProvider.GetRequiredService<ILogger>();

    protected override void BuildUp(object instance)
    {
        // No-op for ServiceProvider
    }

    protected override IEnumerable<object> GetAllInstances(Type service)
    {
        return _serviceProvider.GetServices(service)!;
    }

    protected override object GetInstance(Type service, string key)
    {
        if (service == null)
            throw new ArgumentNullException(nameof(service));

        return _serviceProvider.GetRequiredService(service);
    }

    protected override void OnStartup(object sender, StartupEventArgs e)
    {
        CheckIfApplicationIsRunning()
            .IfSuccess(() => TryStartNvidia()
                .IfSuccess(() => TryLoad()
                    .IfSuccess(() =>
                    {
                        DisplayRootViewForAsync<ShellViewModel>();
                        _fileLogger.Info("Loaded root.");
                        TrimMemory();
                    })));
    }

    public static void TrimMemory()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect();
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch { }
    }

    private static System.Threading.Mutex? _singleInstanceMutex;

    private Result CheckIfApplicationIsRunning()
    {
        bool createdNew;
        _singleInstanceMutex = new System.Threading.Mutex(true, @"Global\DisplayFX_SingleInstance_Mutex", out createdNew);

        var currentProcess = Process.GetCurrentProcess();
        var existingProcess = Process.GetProcessesByName(currentProcess.ProcessName)
                                    .FirstOrDefault(p => p.Id != currentProcess.Id);

        if (createdNew && existingProcess == null) return Result.Ok();

        _fileLogger.Info("Another instance of DisplayFX is already running.");

        IntPtr foundHandle = IntPtr.Zero;

        EnumWindows((hWnd, lParam) =>
        {
            StringBuilder sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            string title = sb.ToString();

            if (title.StartsWith("DisplayFX") || title.StartsWith("Adjust Displays") || title.Contains("DisplayFX")) 
            {
                foundHandle = hWnd;
                return false; 
            }
            return true;
        }, IntPtr.Zero);

        if (foundHandle != IntPtr.Zero)
        {
            _fileLogger.Info("Found existing window via title match. Restoring...");
            ShowWindow(foundHandle, SW_RESTORE);
            SetForegroundWindow(foundHandle);
            PostMessage(foundHandle, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
        }

        Application.Current.Shutdown();
        return Result.Fail("Another instance is already running.");
    }

    private Result TryStartNvidia()
    {
        try
        {
            NVIDIA.Initialize();
            _fileLogger.Info("Starting Nvidia.");
        }
        catch (Exception e)
        {
            _fileLogger.Warn(e, "Nvidia device initialization failed or non-Nvidia GPU detected.");
        }
        return Result.Ok();
    }

    private Result Log(Exception e, string message)
    {
        _fileLogger.Error(e, message);
        Execute.OnUIThread(() =>
        {
            MessageBox.Show(message, "DisplayFX Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current?.Shutdown();
        });

        return Result.Fail(message);
    }

    private Result TryLoad()
    {
        try
        {
            return _dataController.Load()
                .IfFail(Start)
                .Bind(_ => Result.Ok());
        }
        catch (Exception e)
        {
            return Log(e, "Failed to load data.");
        }
    }

    private Result Start()
    {
        try
        {
            _fileLogger.Info("Loading data.");
            return _computerFactory
                .Create()
                .IfSuccess(computer => _dataController.Write(computer))
                .ToResult();
        }
        catch (Exception e)
        {
            return Log(e, "Failed to load data.");
        }
    }

    protected override void PrepareApplication()
    {
        AppDomain.CurrentDomain.UnhandledException += OnError;
        base.PrepareApplication();
    }

    private void OnError(object sender, UnhandledExceptionEventArgs e)
    {
        Log((Exception)e.ExceptionObject, "An unexpected error has occured.");
    }

    protected override void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _fileLogger.Error(e);
    }
}