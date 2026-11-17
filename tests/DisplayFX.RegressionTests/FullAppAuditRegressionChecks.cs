using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caliburn.Micro;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.BrightnessFlyout;
using DisplayFX.Interface.Monitors;
using DisplayFX.Interface.Profiles;
using DisplayFX.Interface.Shell;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.Factories;
using NLog;
using WindowsDisplayAPI;
using M = DisplayFX.Objects.Entities.Monitor;

internal static class FullAppAuditRegressionChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly ILogger Logger = NLog.LogManager.GetLogger("FullAppAudit");
    public static int Run()
    {
        var count = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); count++; }
        var directory = Path.Combine(AppContext.BaseDirectory, "full-audit-" + Guid.NewGuid().ToString("N"));
        var platform = PlatformProvider.Current;
        PlatformProvider.Current = new DefaultPlatformProvider();
        try
        {
            var display = new FakeDisplay();
            var brightness = new FakeBrightness();
            var cache = new FakeCache(display);
            var data = new DataController(directory, directory + ".absent");
            var persistence = new BrightnessPersistenceController(data, cache, brightness, Logger);
            persistence.Attach(new Computer());
            using (var row = new BrightnessMonitorViewModel(display, brightness, name: "Desk"))
            {
                brightness.FailReads = true;
                Pump(() => row.RefreshBrightnessAsync());
                Check(row.HasError && row.BrightnessText == "40%" && row.SupportsBrightness,
                    "A cached level must not hide a failed measurement or stop recovery polling.");
                brightness.FailReads = false;
                brightness.Level = 65;
                Pump(() => row.RefreshBrightnessAsync());
                Check(!row.HasError && row.BrightnessText == "65%", "Brightness recovery must replace the cached level and clear its warning.");
            }
            using (var flyout = new BrightnessFlyoutViewModel(brightness, cache, persistence, deferInitialization: false))
            {
                cache.Fail = true;
                Pump(flyout.RefreshDisplaysAsync);
                Check(flyout.LinkStatus != null, "Display enumeration failures must be visible.");
                cache.Fail = false;
                Pump(flyout.RefreshDisplaysAsync);
                Check(flyout.LinkStatus == null, "A recovered flyout must clear its obsolete connection warning.");
            }
            var diagnostics = new ColorUpdateDiagnostics(cache, brightness, Logger) { Enabled = true };
            cache.Fail = true;
            var applications = 0;
            Pump(async () => Check(await diagnostics.ApplyAsync(() => { applications++; return true; }),
                "Optional diagnostics must not block valid colors when enumeration fails."));
            Check(applications == 1 && diagnostics.Warning != null, "Diagnostic failures must warn and apply colors exactly once.");
            cache.Fail = false;
            brightness.ThrowReads = true;
            Pump(async () => Check(await diagnostics.ApplyAsync(() => true), "A broken diagnostic brightness read must not prevent color application."));
            brightness.ThrowReads = false;
            Pump(async () => Check(!await diagnostics.ApplyAsync(() => false) && diagnostics.Warning == null,
                "Diagnostics must preserve a failed color result and clear earlier diagnostic warnings."));

            count += SwitchingChecks();
            count += RemovedProfileChecks(directory, false);
            count += RemovedProfileChecks(directory, true);
            count += TopologyDuringApplyChecks(directory);
            count += DeleteDuringApplyChecks(directory);
            count += ManualApplyChecks(directory);
            var (selectionShell, selectionModel, _, _, selectionGate) = MakeShell(directory);
            typeof(ShellViewModel).GetField("_nvidiaDisplays", Private)!.SetValue(selectionShell,
                new List<NvAPIWrapper.Display.Display> { null! });
            typeof(ShellViewModel).GetMethod("SelectMonitor", Private)!.Invoke(selectionShell, new object[] { selectionModel });
            Check(ReferenceEquals(selectionShell.SelectedMonitor, selectionModel) && selectionShell.SelectedNvidiaMonitor == null && selectionShell.HasOperationStatus,
                "A stale NVIDIA handle must not prevent selecting a monitor or hide its connection error.");
            selectionGate.Dispose();
        }
        finally
        {
            PlatformProvider.Current = platform;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        return count;
    }

    private static int SwitchingChecks()
    {
        var monitor = new M("test", "Desk", new(1920, 1080), 60);
        var manual = new Profile(monitor, "Default", new(.5, .5, 1, .5), true, true);
        var game = new Profile(monitor, "Game", new(.5, .5, 1, .5), false) { LinkedExecutablePath = "game.exe" };
        monitor.Profiles.AddRange(new[] { manual, game });
        var switching = new AppProfileSwitchingController();
        var release = new TaskCompletionSource<bool>();
        var work = switching.UpdateAsync("game.exe", new[] { monitor }, _ => release.Task);
        switching.InvalidateHardware(new[] { monitor });
        manual.IsActive = false; game.IsActive = true; release.SetResult(true);
        work.GetAwaiter().GetResult();
        var calls = 0;
        switching.Update("game.exe", new[] { monitor }, _ => { calls++; return true; });
        if (calls != 1) throw new InvalidOperationException("A topology change during an apply must remain pending for reapplication.");
        switching.Reset(); manual.IsActive = true; game.IsActive = false;
        release = new TaskCompletionSource<bool>();
        work = switching.UpdateAsync("game.exe", new[] { monitor }, _ => release.Task);
        switching.Reset();
        release.SetResult(true); work.GetAwaiter().GetResult();
        calls = 0;
        switching.Update("browser.exe", new[] { monitor }, _ => { calls++; return true; });
        if (calls != 0) throw new InvalidOperationException("A reset during application must not resurrect old automatic baselines.");
        return 2;
    }

    private static int RemovedProfileChecks(string directory, bool queued)
    {
        var (shell, model, profile, colors, gate) = MakeShell(directory);
        Pump(async () =>
        {
            if (queued) await gate.WaitAsync();
            var work = Apply(shell, profile);
            try
            {
                if (!queued) await colors.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                model.Profiles.Remove(profile);
                model.Monitor.Profiles.Remove(profile.Profile);
            }
            finally { if (queued) gate.Release(); else colors.Release.SetResult(true); }
            if (await work || profile.IsActive || model.Monitor.Profiles.Any(p => p.IsActive))
                throw new InvalidOperationException("A removed profile must not be activated by queued or completed GPU work.");
            if (queued && colors.Calls != 0) throw new InvalidOperationException("A removed queued profile must never reach hardware.");
        });
        gate.Dispose();
        return queued ? 2 : 1;
    }

    private static int TopologyDuringApplyChecks(string directory)
    {
        var (shell, model, profile, colors, gate) = MakeShell(directory);
        Pump(async () =>
        {
            var work = Apply(shell, profile);
            try
            {
                await colors.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                typeof(ShellViewModel).GetField("_modelVersion", Private)!.SetValue(shell, 1);
            }
            finally { colors.Release.SetResult(true); }
            if (await work || profile.IsActive) throw new InvalidOperationException("Old GPU completions must not activate profiles after topology invalidation.");
        });
        gate.Dispose();
        return 1;
    }

    private static int DeleteDuringApplyChecks(string directory)
    {
        var (shell, model, profile, colors, gate) = MakeShell(directory);
        var savedDefault = new Profile(model.Monitor, "Default", new(.5, .5, 1, .5), true, true);
        var defaultView = new ProfileViewModel(savedDefault, model, new ProfileSettingViewModelFactory(new EventAggregator()));
        model.Monitor.Profiles.Add(savedDefault); model.Profiles.Add(defaultView);
        Pump(async () =>
        {
            var apply = Apply(shell, profile);
            Task? removal = null;
            try
            {
                await colors.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                removal = (Task)typeof(ShellViewModel).GetMethod("OnProfileRemoved", Private)!.Invoke(shell, new object[] { profile.Guid })!;
                if (removal.IsCompleted) throw new InvalidOperationException("Deleting a pending preview must wait for color application.");
            }
            finally { colors.Release.TrySetResult(true); }
            await apply;
            await removal!;
            if (model.Profiles.Contains(profile) || !defaultView.IsActive || colors.Calls != 2)
                throw new InvalidOperationException("Deleting a pending preview must restore default colors before removing the profile.");
        });
        gate.Dispose();
        return 2;
    }

    private static int ManualApplyChecks(string directory)
    {
        var (shell, monitor, profile, colors, gate) = MakeShell(Path.Combine(directory, "manual-apply"));
        using (gate)
        {
            var other = new ProfileViewModel(new Profile(monitor.Monitor, "Other", new(.5, .5, 1, .5), false),
                monitor, new ProfileSettingViewModelFactory(new EventAggregator()));
            monitor.Monitor.Profiles.Add(other.Profile);
            monitor.Profiles.Add(other);
            shell.SelectedMonitor = monitor;
            shell.SelectedProfile = profile;
            Pump(async () =>
            {
                var apply = shell.Apply();
                try
                {
                    await colors.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    if (shell.CanApply || shell.CanSave || shell.ApplyActionLabel != "Applying…")
                        throw new InvalidOperationException("An in-flight application must show a stable button label and block repeat Apply and Save.");
                    var repeat = shell.Apply();
                    if (!repeat.IsCompletedSuccessfully)
                        throw new InvalidOperationException("Repeat Apply clicks must not enqueue additional hardware work.");
                    shell.SelectedProfile = other;
                }
                finally { colors.Release.TrySetResult(true); }
                await apply;
                if (colors.Calls != 1 || !profile.IsActive || shell.SelectedProfile != other || shell.ApplyActionLabel != "Apply" ||
                    shell.OperationStatus != "Profile applied." || !shell.CanApply)
                    throw new InvalidOperationException("Apply completion must report success without changing the user's new selection.");
            });
        }
        return 4;
    }

    private static (ShellViewModel, MonitorViewModel, ProfileViewModel, FakeColors, SemaphoreSlim) MakeShell(string directory, ShellViewModel? shell = null)
    {
        var monitor = new M("test", "Desk", new(1920, 1080), 60);
        var saved = new Profile(monitor, "Game", new(.5, .5, 1, .5), false);
        monitor.Profiles.Add(saved);
        var model = new MonitorViewModel(monitor, new FakeDisplay());
        var profile = new ProfileViewModel(saved, model, new ProfileSettingViewModelFactory(new EventAggregator()));
        model.Profiles.Add(profile);
        shell ??= (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
        void Set(string name, object value) => typeof(ShellViewModel).GetField(name, Private)!.SetValue(shell, value);
        var colors = new FakeColors();
        var gate = new SemaphoreSlim(1, 1);
        Set("_computer", new Computer { Monitors = new() { monitor } });
        Set("_monitors", new ObservableCollection<MonitorViewModel> { model });
        Set("_logger", Logger); Set("_profileRequests", new Dictionary<M, long>()); Set("_colorGate", gate);
        Set("_eventAggregator", new EventAggregator());
        Set("_displayController", colors); Set("_appSwitching", new AppProfileSwitchingController());
        Set("_dataController", new DataController(directory, directory + ".absent"));
        Set("_colorDiagnostics", new ColorUpdateDiagnostics(new FakeCache(), new FakeBrightness(), Logger));
        return (shell, model, profile, colors, gate);
    }
    private static Task<bool> Apply(ShellViewModel shell, ProfileViewModel profile) =>
        (Task<bool>)typeof(ShellViewModel).GetMethod("ApplyProfile", Private)!.Invoke(shell, new object?[] { profile, false, null })!;
    private static void Pump(Func<Task> action) => BrightnessFeatureRegressionChecks.Pump(action);
    private sealed class FakeDisplay : Display { public FakeDisplay() : base(new FakeDevice()) { } }
    private sealed class FakeDevice : DisplayDevice { public FakeDevice() : base("test", "Test", "Test") { } }
    private sealed class FakeCache : DisplayCache
    {
        private readonly Display[] _displays;
        public bool Fail;
        public FakeCache(params Display[] displays) => _displays = displays;
        public override void Refresh() { if (Fail) throw new InvalidOperationException("Enumeration unavailable"); }
        public override IReadOnlyList<Display> GetDisplays() => Fail ? throw new InvalidOperationException("Enumeration unavailable") : _displays;
    }
    private sealed class FakeBrightness : MonitorBrightnessController
    {
        public FakeBrightness() : base(Logger) { }
        public bool FailReads, ThrowReads;
        public int Level = 40;
        public override bool SupportsBrightness(Display display) => true;
        public override int? GetBrightness(Display display)
        {
            if (ThrowReads) throw new InvalidOperationException("Read unavailable");
            if (FailReads) return null;
            RecordStatus(display, new("", false, Level));
            return Level;
        }
    }
    private sealed class FakeColors : DisplayController
    {
        public readonly TaskCompletionSource<bool> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public FakeColors() : base(Logger, null!) { }
        public override bool UpdateColorSettings(Display display, ProfileSetting settings, NvAPIWrapper.Display.Display? nvidia)
        {
            Calls++; Entered.TrySetResult(true);
            return Release.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
    }

}
