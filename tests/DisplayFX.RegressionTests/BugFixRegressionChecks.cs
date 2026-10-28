using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using Caliburn.Micro;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.BrightnessFlyout;
using DisplayFX.Interface.Settings;
using DisplayFX.Objects.Entities;
using NLog;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;
using WindowsDisplayAPI.Exceptions;
using Monitor = DisplayFX.Objects.Entities.Monitor;
using Action = System.Action;

/// <summary>Behavior checks for the repository audit findings, with no physical monitor or native hotkey writes.</summary>
internal static class BugFixRegressionChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly ILogger Logger = NLog.LogManager.GetLogger("BugFixChecks");

    public static int Run()
    {
        var count = ImportChecks() + NativeModeChecks();
        var directory = Path.Combine(AppContext.BaseDirectory, "bug-fixes-" + Guid.NewGuid().ToString("N"));
        var platform = PlatformProvider.Current;
        PlatformProvider.Current = new DefaultPlatformProvider();
        try { count += BrightnessChecks(directory); }
        finally
        {
            PlatformProvider.Current = platform;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        return count;
    }

    private static int ImportChecks()
    {
        var count = 0;
        foreach (var name in new[] { "Default", new string('x', 80), "Game (2)" })
        {
            var current = MakeComputer();
            current.Monitors[0].Profiles[0].Name = name;
            var imported = MakeComputer();
            imported.Monitors[0].Profiles[0].Name = name;
            imported.Monitors[0].Profiles[0].ProfileSetting.Brightness = .2;
            var first = ProfileTransferController.MergeSettings(current, imported);
            var expected = first.Monitors[0].Profiles[1].Name;
            for (var attempt = 0; attempt < 6; attempt++)
                first = ProfileTransferController.MergeSettings(first, imported);
            Check(first.Monitors[0].Profiles.Count == 2 && first.Monitors[0].Profiles[1].Name == expected,
                "Reimporting a renamed profile must not consume additional capacity, including truncated and suffixed names.");
            count++;
            first.Monitors[0].Profiles[1].ProfileSetting.Brightness = .3;
            var changed = ProfileTransferController.MergeSettings(first, imported);
            Check(changed.Monitors[0].Profiles.Count == 3 && changed.Monitors[0].Profiles[2].ProfileSetting.Brightness == .2,
                "A modified imported copy must not suppress restoration of the backed-up values.");
            count++;
        }
        var distinct = MakeComputer();
        distinct.Monitors[0].Profiles.Add(new Profile(distinct.Monitors[0], "Unrelated", new(.2,.5,1,.5), false));
        var backup = MakeComputer();
        backup.Monitors[0].Profiles[0].ProfileSetting.Brightness = .2;
        var merged = ProfileTransferController.MergeSettings(distinct, backup);
        Check(merged.Monitors[0].Profiles.Count == 3, "Equal settings with an unrelated profile name must remain distinct.");
        count++;

        // Different long source names can produce the same truncated copy-name prefix.
        var longCurrent = MakeComputer();
        var longBackup = MakeComputer();
        var prefix = new string('z', 79);
        foreach (var model in new[] { longCurrent, longBackup })
        {
            var monitor = model.Monitors[0];
            monitor.Profiles[0].Name = prefix + "A";
            monitor.Profiles.Add(new Profile(monitor, prefix + "B", new(.5,.5,1,.5),false));
        }
        foreach (var profile in longBackup.Monitors[0].Profiles) profile.ProfileSetting.Brightness = .2;
        var longMerged = ProfileTransferController.MergeSettings(longCurrent, longBackup);
        Check(longMerged.Monitors[0].Profiles.Count == 4 && longMerged.Monitors[0].Profiles[2].ImportedProfileName !=
            longMerged.Monitors[0].Profiles[3].ImportedProfileName, "Truncation must not collapse distinct imported profile names.");
        count++;
        for (var attempt = 0; attempt < 6; attempt++) longMerged = ProfileTransferController.MergeSettings(longMerged, longBackup);
        Check(longMerged.Monitors[0].Profiles.Count == 4, "Import provenance must survive serialization and keep repeated long-name collisions stable.");
        return count + 1;
    }

    private static int NativeModeChecks()
    {
        var assembly = typeof(PathInfo).Assembly;
        var modeType = assembly.GetType("WindowsDisplayAPI.Native.DisplayConfig.Structures.DisplayConfigModeInfo", true)!;
        var sourceType = assembly.GetType("WindowsDisplayAPI.Native.DisplayConfig.Structures.DisplayConfigSourceMode", true)!;
        var sourceConstructor = sourceType.GetConstructors()[0];
        var parameters = sourceConstructor.GetParameters();
        object MakeMode(uint width)
        {
            var source = sourceConstructor.Invoke(new object[] { width, 1080u,
                Enum.ToObject(parameters[2].ParameterType, 4), Activator.CreateInstance(parameters[3].ParameterType)! });
            var constructor = modeType.GetConstructors().Single(c => c.GetParameters()[2].ParameterType == sourceType);
            return constructor.Invoke(new object[] { Activator.CreateInstance(constructor.GetParameters()[0].ParameterType)!, 0u, source });
        }
        var modes = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(modeType))!;
        var add = typeof(PathInfo).GetMethod("AddMode", BindingFlags.Static | BindingFlags.NonPublic)!;
        object[] arguments = { modes, MakeMode(1920) };
        Check((uint)add.Invoke(null, arguments)! == 0 && (uint)add.Invoke(null, arguments)! == 0 && modes.Count == 1,
            "The native source mode at index zero must be reused.");
        arguments[1] = MakeMode(2560);
        try { add.Invoke(null, arguments); throw new InvalidOperationException("Conflicting first source mode must be rejected."); }
        catch (TargetInvocationException exception) when (exception.InnerException is DuplicateModeException) { }
        Check(modes.Count == 1, "Rejected conflicting modes must leave the mode list unchanged.");
        return 3;
    }

    private static int BrightnessChecks(string directory)
    {
        var count = 0;
        void Verify(bool condition, string message) { Check(condition, message); count++; }
        var first = new FakeDisplay("first");
        var second = new FakeDisplay("second");
        var hardware = new FakeBrightness();
        var cache = new FakeCache { Displays = new[] { first, second } };
        var data = new DataController(directory, Path.Combine(directory, "missing.json"));
        var computer = MakeComputer();
        var persistence = new BrightnessPersistenceController(data, cache, hardware, Logger);
        persistence.Attach(computer);

        hardware.Readable = false;
        using (var row = new BrightnessMonitorViewModel(first, hardware, name: "Write only"))
        {
            BrightnessFeatureRegressionChecks.Pump(() => row.RefreshBrightnessAsync());
            Verify(row.SupportsBrightness && row.BrightnessText == "—", "Read failure must preserve write capability without inventing a level.");
            row.Brightness = 63;
            BrightnessFeatureRegressionChecks.Pump(() => row.FlushPendingBrightnessAsync());
            Verify(hardware.Levels["first"] == 63 && !row.HasError, "A write-only monitor must still accept slider input after recovery.");
        }
        hardware.Readable = true;
        hardware.Writes.Clear();
        hardware.Levels["first"] = 40;
        hardware.Levels["second"] = 40;

        using (var flyout = new BrightnessFlyoutViewModel(hardware, cache, persistence, deferInitialization: false))
        {
            var removed = flyout.Monitors[1];
            removed.Brightness = 77;
            cache.Displays = new[] { first };
            BrightnessFeatureRegressionChecks.Pump(async () =>
            {
                await flyout.RefreshDisplaysAsync();
                await removed.FlushPendingBrightnessAsync();
            });
            Verify(flyout.Monitors.Count == 1 && hardware.Writes.Count == 0,
                "Disconnected rows must be removed and their unstarted writes discarded.");
            removed.Brightness = 88;
            BrightnessFeatureRegressionChecks.Pump(() => removed.FlushPendingBrightnessAsync());
            Verify(hardware.Writes.Count == 0, "A disposed disconnected row must reject further input.");
            cache.Displays = new[] { first, second };
            BrightnessFeatureRegressionChecks.Pump(() => flyout.RefreshDisplaysAsync());
            Verify(flyout.Monitors.Count == 2 && !ReferenceEquals(removed, flyout.Monitors[1]) && flyout.Monitors[1].SupportsBrightness,
                "A reconnect must create a usable row without reviving disposed controls.");
        }

        cache.Displays = new[] { first };
        using var automation = new FakeAutomation(cache, hardware, persistence);
        automation.Attach(computer);
        var uiThread = Environment.CurrentManagedThreadId;
        var feedbackThreads = new List<int>();
        automation.StatusChanged += () => feedbackThreads.Add(Environment.CurrentManagedThreadId);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        hardware.BeforeWrite = () => { started.Set(); if (!release.Wait(5000)) throw new TimeoutException("Blocked fake write was not released."); };
        BrightnessFeatureRegressionChecks.Pump(async () =>
        {
            var work = automation.AdjustBrightnessAsync(5);
            Verify(!work.IsCompleted, "Shortcut work must return before monitor I/O completes.");
            try
            {
                if (!await Task.Run(() => started.Wait(5000))) throw new TimeoutException("Fake write did not start.");
                var responsive = false;
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => responsive = true);
                Verify(responsive && hardware.WriteThread != uiThread && cache.RefreshThread != uiThread,
                    "The dispatcher must remain usable while enumeration and hardware I/O run elsewhere.");
                _ = automation.AdjustBrightnessAsync(10);
                _ = automation.AdjustBrightnessAsync(10);
                _ = automation.AdjustBrightnessAsync(10);
            }
            finally { release.Set(); }
            await work;
        });
        hardware.BeforeWrite = null;
        Verify(hardware.Writes.Select(write => write.Level).SequenceEqual(new[] { 45, 75 }),
            "Rapid shortcuts must combine pending steps while retaining the requested cumulative change.");
        Verify(computer.Monitors[0].LastBrightness == 75 && data.Load().Value.Monitors[0].LastBrightness == 75 &&
               feedbackThreads.All(thread => thread == uiThread), "Successful automation results and feedback must reach the UI-owned settings model.");
        Verify(hardware.MaxConcurrentWrites == 1, "Automation writes must never overlap.");

        hardware.Writes.Clear();
        hardware.Levels["first"] = 90;
        BrightnessFeatureRegressionChecks.Pump(async () =>
        {
            var work = automation.AdjustBrightnessAsync(20);
            _ = automation.AdjustBrightnessAsync(-20);
            await work;
        });
        Verify(hardware.Levels["first"] == 80, "Coalescing must preserve a direction reversal after clamping at 100.");

        var dispatcher = Dispatcher.CurrentDispatcher;
        hardware.AfterWrite = () => dispatcher.BeginInvoke(new Action(() =>
        {
            hardware.Levels["first"] = 95;
            hardware.ExternalStatus(first, 95);
            persistence.Remember(first, 95);
        }), DispatcherPriority.Send);
        BrightnessFeatureRegressionChecks.Pump(() => automation.AdjustBrightnessAsync(-5));
        hardware.AfterWrite = null;
        Verify(computer.Monitors[0].LastBrightness == 95 && data.Load().Value.Monitors[0].LastBrightness == 95,
            "A delayed automation completion must not overwrite a newer brightness change from the slider.");

        // An accepted physical write can finish during reset; stale completion must not save into the replacement model.
        hardware.Writes.Clear(); started.Reset(); release.Reset();
        hardware.BeforeWrite = () => { started.Set(); if (!release.Wait(5000)) throw new TimeoutException(); };
        BrightnessFeatureRegressionChecks.Pump(async () =>
        {
            var work = automation.AdjustBrightnessAsync(1);
            try
            {
                if (!await Task.Run(() => started.Wait(5000))) throw new TimeoutException();
                _ = automation.AdjustBrightnessAsync(20);
                automation.Detach();
            }
            finally { release.Set(); }
            await work;
        });
        hardware.BeforeWrite = null;
        Verify(hardware.Writes.Count == 1 && computer.Monitors[0].LastBrightness == 95,
            "Detach must discard queued requests and prevent a stale in-flight completion from persisting.");

        automation.Attach(computer);
        var day = new DateTime(2026, 10, 15);
        automation.Now = day.AddHours(7);
        computer.IsBrightnessScheduleEnabled = true;
        computer.BrightnessSchedules = new() { new() { Time = "08:00", Brightness = 80 }, new() { Time = "20:00", Brightness = 20 } };
        automation.Start();
        hardware.Writes.Clear();
        hardware.NotifyDisplayPower(false);
        automation.Now = day.AddHours(9); automation.Tick();
        automation.Now = day.AddHours(21); automation.Tick();
        Verify(hardware.Writes.Count == 0, "Schedules due during display sleep must not write sleeping hardware.");
        BrightnessFeatureRegressionChecks.Pump(async () =>
        {
            hardware.NotifyDisplayPower(true);
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            await automation.PendingWork;
        });
        Verify(hardware.Writes.Select(write => write.Level).SequenceEqual(new[] { 20 }),
            "Wake must immediately apply only the latest due schedule without waiting for the next daily occurrence.");
        BrightnessFeatureRegressionChecks.Pump(async () => { automation.Tick(); await automation.PendingWork; });
        Verify(hardware.Writes.Count == 1, "Subsequent ticks must not repeat the recovered schedule.");
        hardware.NotifyDisplayPower(false);
        automation.Now = day.AddDays(1).AddHours(9); automation.Tick();
        computer.IsBrightnessScheduleEnabled = false;
        automation.SettingsChanged();
        BrightnessFeatureRegressionChecks.Pump(async ()
            => { hardware.NotifyDisplayPower(true); await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background); await automation.PendingWork; });
        Verify(hardware.Writes.Count == 1, "Disabling a schedule during sleep must cancel catch-up.");

        computer.IsBrightnessShortcutsEnabled = true;
        automation.FailRegistration = true;
        automation.RefreshHotkeys();
        var settings = new SettingsViewModel(computer, new RegistryController(), data, automation);
        Verify(settings.HasShortcutError && settings.ShortcutStatus.Contains("another app"), "Native registration failures must reach Settings.");
        automation.SuspendHotkeys(true);
        Verify(settings.HasShortcutError && settings.ShortcutStatus.Contains("another app"),
            "Opening Settings and temporarily releasing hotkeys must not erase the previous failure.");
        automation.FailRegistration = false;
        automation.SuspendHotkeys(false);
        Verify(!settings.HasShortcutError && automation.ShortcutError == null, "A successful retry must clear obsolete registration feedback.");
        return count;
    }

    private static Computer MakeComputer()
    {
        var monitor = new Monitor("first", "First", new Size(1920,1080),60);
        monitor.Profiles.Add(new Profile(monitor, "Default", new(.5,.5,1,.5), true, true));
        return new Computer { Monitors = new() { monitor } };
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class FakeCache : DisplayCache
    {
        public IReadOnlyList<Display> Displays = Array.Empty<Display>();
        public int RefreshThread;
        public override void Refresh() => RefreshThread = Environment.CurrentManagedThreadId;
        public override IReadOnlyList<Display> GetDisplays() => Displays;
    }
    private sealed class FakeBrightness : MonitorBrightnessController
    {
        public FakeBrightness() : base(Logger) { }
        public readonly ConcurrentDictionary<string,int> Levels = new(new[] { new KeyValuePair<string,int>("first",40), new("second",40) });
        public readonly ConcurrentQueue<(string Path, int Level)> Writes = new();
        public bool Readable = true;
        public Action? BeforeWrite;
        public Action? AfterWrite;
        public int WriteThread;
        private int _concurrent;
        public int MaxConcurrentWrites;
        public override int? GetBrightness(Display display) => Readable ? Levels[display.DevicePath] : null;
        public override bool SupportsBrightness(Display display) => !IsDisplaySleeping;
        public override bool SetBrightness(Display display, int brightness)
        {
            var concurrent = Interlocked.Increment(ref _concurrent);
            MaxConcurrentWrites = Math.Max(MaxConcurrentWrites, concurrent);
            try
            {
                WriteThread = Environment.CurrentManagedThreadId;
                BeforeWrite?.Invoke();
                Levels[display.DevicePath] = brightness;
                Writes.Enqueue((display.DevicePath,brightness));
                RecordStatus(display, new BrightnessStatus("", false, brightness));
                AfterWrite?.Invoke();
                return true;
            }
            finally { Interlocked.Decrement(ref _concurrent); }
        }
        public void ExternalStatus(Display display, int brightness) => RecordStatus(display, new BrightnessStatus("", false, brightness));
    }
    private sealed class FakeAutomation : BrightnessAutomationController
    {
        public FakeAutomation(DisplayCache cache, MonitorBrightnessController hardware, BrightnessPersistenceController persistence)
            : base(cache, hardware, persistence, Logger) { }
        public DateTime Now = new(2026,10,15);
        public bool FailRegistration;
        protected override DateTime LocalNow => Now;
        public Task PendingWork => (Task)typeof(BrightnessAutomationController).GetField("_worker", PrivateInstance)!.GetValue(this)!;
        public void Tick() => typeof(BrightnessAutomationController).GetMethod("OnTimerTick", PrivateInstance)!.Invoke(this, new object?[] { null, EventArgs.Empty });
        protected override void RegisterHotkey(string id, Key key, ModifierKeys modifiers, int direction)
        { if (FailRegistration) throw new InvalidOperationException("Hotkey already registered by another app."); }
        protected override void RemoveHotkeys() { }
    }
    private sealed class FakeDisplay : Display
    { public FakeDisplay(string path) : base(new FakeDevice(path)) { } public override bool IsAvailable => false; }
    private sealed class FakeDevice : DisplayDevice
    { public FakeDevice(string path) : base(path, "Regression display", "Regression key") { } }
}
