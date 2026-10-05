using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
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
using DisplayFX.Interface.BrightnessFlyout;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

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
        services.AddSingleton<RegistryController>();
        services.AddSingleton<MonitorBrightnessController>();
        services.AddSingleton<ProcessController>();
        services.AddSingleton<AppProfileSwitchingController>();
        services.AddSingleton<BrightnessPersistenceController>();
        services.AddSingleton<BrightnessAutomationController>();
        services.AddSingleton<MonitorTopologyController>();
        services.AddSingleton<MonitorIdentificationService>();
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
        services.AddTransient<BrightnessFlyoutViewModel>();
        services.AddTransient<ShellViewModel>();
    }

    private ComputerFactory _computerFactory => _serviceProvider.GetRequiredService<ComputerFactory>();
    private DataController _dataController => _serviceProvider.GetRequiredService<DataController>();
    private BrightnessPersistenceController _brightnessPersistenceController =>
        _serviceProvider.GetRequiredService<BrightnessPersistenceController>();
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

    protected override async void OnStartup(object sender, StartupEventArgs e)
    {
        try
        {
            if ((await CheckIfApplicationIsRunning(e.Args)).IsFailed)
                return;
            TryStartNvidia();
            if (TryLoad().IsFailed)
                return;

            _brightnessPersistenceController.RestoreOnStartup();
            await DisplayRootViewForAsync<ShellViewModel>();
            _fileLogger.Info("Loaded root.");
            TrimMemory();
        }
        catch (Exception exception)
        {
            Log(exception, "Failed to start DisplayFX.");
        }
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

    public static void RestartApplication(System.Action? beforeShutdown = null)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the DisplayFX executable.");
        var assemblyPath = Assembly.GetEntryAssembly()?.Location ?? Assembly.GetExecutingAssembly().Location;
        var startInfo = CreateRestartStartInfo(processPath, assemblyPath, Environment.ProcessId);
        using var restartedProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch the replacement DisplayFX process.");
        beforeShutdown?.Invoke();
        Application.Current.Shutdown();
    }

    internal static ProcessStartInfo CreateRestartStartInfo(string processPath, string assemblyPath, int parentProcessId)
    {
        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("--restart");
        startInfo.ArgumentList.Add(parentProcessId.ToString(CultureInfo.InvariantCulture));
        return startInfo;
    }

    internal static int? GetRestartParentProcessId(IReadOnlyList<string> arguments, int currentProcessId)
    {
        if (arguments.Count != 2 || arguments[0] != "--restart" ||
            !int.TryParse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parentProcessId) ||
            parentProcessId <= 0 || parentProcessId == currentProcessId)
            return null;

        return parentProcessId;
    }

    internal static bool IsApplicationWindow(uint processId, string title, ISet<int> applicationProcessIds)
    {
        return processId <= int.MaxValue && applicationProcessIds.Contains((int)processId) &&
               (title.StartsWith("DisplayFX", StringComparison.Ordinal) ||
                title.StartsWith("Adjust Displays", StringComparison.Ordinal));
    }

    internal static bool MatchesApplicationIdentity(string candidateName, int candidateSession, string? candidatePath,
        string currentName, int currentSession, string? currentPath, bool requireSamePath)
    {
        if (candidateSession != currentSession ||
            !string.Equals(candidateName, currentName, StringComparison.OrdinalIgnoreCase))
            return false;

        return !requireSamePath ||
               (!string.IsNullOrWhiteSpace(candidatePath) && !string.IsNullOrWhiteSpace(currentPath) &&
                string.Equals(candidatePath, currentPath, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSameApplication(Process candidate, Process current, bool requireSamePath = true)
    {
        try
        {
            return MatchesApplicationIdentity(candidate.ProcessName, candidate.SessionId,
                requireSamePath ? candidate.MainModule?.FileName : null,
                current.ProcessName, current.SessionId, requireSamePath ? current.MainModule?.FileName : null,
                requireSamePath);
        }
        catch (Exception exception) when (exception is InvalidOperationException ||
                                          exception is System.ComponentModel.Win32Exception || exception is NotSupportedException)
        {
            return false;
        }
    }

    private static async Task WaitForRestartParentAsync(int parentProcessId, Process current)
    {
        Process parent;
        try
        {
            parent = Process.GetProcessById(parentProcessId);
        }
        catch (ArgumentException)
        {
            // The original process completed its shutdown before this child started.
            return;
        }

        using (parent)
        {
            if (parent.HasExited)
                return;
            if (!IsSameApplication(parent, current))
            {
                if (parent.HasExited)
                    return;
                throw new InvalidOperationException("The restart parent is not DisplayFX in this Windows session.");
            }

            try
            {
                using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                await parent.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException exception)
            {
                throw new InvalidOperationException("The previous DisplayFX instance did not close within 10 seconds. Close it and open DisplayFX again.", exception);
            }
        }
    }

    private async Task<Result> CheckIfApplicationIsRunning(IReadOnlyList<string> arguments)
    {
        using var currentProcess = Process.GetCurrentProcess();
        var restartParentId = GetRestartParentProcessId(arguments, currentProcess.Id);
        if (restartParentId.HasValue)
            await WaitForRestartParentAsync(restartParentId.Value, currentProcess);

        _singleInstanceMutex = new System.Threading.Mutex(true, @"Local\DisplayFX_SingleInstance_Mutex", out var createdNew);
        if (createdNew)
            return Result.Ok();

        var existingProcessIds = new HashSet<int>();
        foreach (var candidate in Process.GetProcessesByName(currentProcess.ProcessName))
        {
            using (candidate)
            {
                if (candidate.Id != currentProcess.Id && IsSameApplication(candidate, currentProcess, false))
                    existingProcessIds.Add(candidate.Id);
            }
        }

        _fileLogger.Info("Another instance of DisplayFX is already running.");

        IntPtr foundHandle = IntPtr.Zero;

        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out var processId);
            if (processId > int.MaxValue || !existingProcessIds.Contains((int)processId))
                return true;

            StringBuilder sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            string title = sb.ToString();

            if (IsApplicationWindow(processId, title, existingProcessIds))
            {
                foundHandle = hWnd;
                return false; 
            }
            return true;
        }, IntPtr.Zero);

        if (foundHandle != IntPtr.Zero)
        {
            _fileLogger.Info("Found the existing DisplayFX process window. Restoring...");
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
            var loaded = _dataController.Load();
            if (loaded.IsSuccess)
            {
                var detected = _computerFactory.Create();
                if (detected.IsSuccess && ComputerFactory.MergeConnectedMonitors(loaded.Value, detected.Value))
                    _dataController.Write(loaded.Value);
                else if (detected.IsFailed)
                    _fileLogger.Warn("Could not refresh connected monitors; retaining saved monitor settings.");
                return Result.Ok();
            }
            if (!_dataController.HasSavedData)
                return Start();

            return Log(new InvalidOperationException(string.Join("; ", loaded.Errors.Select(error => error.Message))),
                $"Failed to load saved settings. Your files have been preserved.\n{_dataController.DataPath}\n" +
                string.Join("; ", loaded.Errors.Select(error => error.Message)));
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
            var created = _computerFactory.Create();
            if (created.IsFailed)
                return Log(new InvalidOperationException(string.Join("; ", created.Errors.Select(error => error.Message))),
                    "Failed to initialize monitor settings.");
            _dataController.Write(created.Value);
            return Result.Ok();
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

    protected override void OnExit(object sender, EventArgs e)
    {
        _singleInstanceMutex?.Dispose();
        _serviceProvider.Dispose();
        NLog.LogManager.Shutdown();
        base.OnExit(sender, e);
    }
}
