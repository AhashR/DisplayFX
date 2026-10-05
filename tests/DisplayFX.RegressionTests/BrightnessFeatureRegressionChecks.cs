using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using System.Threading;
using Action = System.Action;
using Caliburn.Micro;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.BrightnessFlyout;
using DisplayFX.Interface.Settings;
using DisplayFX.Interface.Shell;
using DisplayFX.Objects.Entities;
using NLog;
using LogManager = NLog.LogManager;
using WindowsDisplayAPI;
using Monitor = DisplayFX.Objects.Entities.Monitor;

internal static class BrightnessFeatureRegressionChecks
{
    public static int Run()
    {
        var count = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            count++;
        }
        var morning = new BrightnessSchedule { Time = "08:00", Brightness = 80 };
        var evening = new BrightnessSchedule { Time = "20:30", Brightness = 30 };
        var schedules = new[] { morning, evening };
        var day = new DateTime(2026, 10, 4);
        BrightnessSchedule? Due(DateTime before, DateTime now) =>
            BrightnessAutomationController.FindLatestDueSchedule(schedules, before, now);
        Check(morning.IsValid && morning.TryGetTime(out var time) && time == TimeSpan.FromHours(8), "HH:mm schedule must parse.");
        foreach (var invalid in new[] { "24:00", "25:01", "12:60", "8:00", "08:0", "", " 08:00", "08:00:00" })
            Check(!new BrightnessSchedule { Time = invalid }.IsValid, "Malformed or out-of-day times must be rejected: " + invalid);
        Check(!new BrightnessSchedule { Brightness = -1 }.IsValid && !new BrightnessSchedule { Brightness = 101 }.IsValid,
            "Schedule levels must stay in the physical brightness range.");
        Check(Due(day.AddHours(7), day.AddHours(8)) == morning, "A schedule is due exactly at its time.");
        Check(Due(day.AddHours(8), day.AddHours(9)) == null, "An occurrence already checked must not repeat.");
        Check(Due(day.AddHours(7), day.AddHours(22)) == evening, "Wake from sleep must apply only the latest due level.");
        Check(Due(day.AddDays(-1).AddHours(20), day.AddHours(1)) == evening, "Midnight must include a due event from yesterday.");
        Check(Due(day.AddDays(-3), day.AddHours(10)) == morning, "Multi-day sleep must apply the latest occurrence only.");
        Check(Due(day.AddHours(10), day.AddHours(7)) == null && Due(day, day) == null, "Backward or unchanged clocks must not trigger.");
        Check(BrightnessAutomationController.FindLatestDueSchedule(new[] { new BrightnessSchedule { Time = "invalid" }, morning },
            day.AddHours(7), day.AddHours(9)) == morning, "Invalid entries must not block valid due schedules.");
        Check(BrightnessAutomationController.FindLatestDueSchedule(null, day, day.AddDays(1)) == null, "Missing schedules must be safe.");

        var computer = new Computer { IsBrightnessShortcutsEnabled = true };
        var monitor = new Monitor("first", "First", new System.Drawing.Size(1920, 1080), 60) { CustomName = "Desk" };
        var other = new Monitor("second", "Second", new System.Drawing.Size(1920, 1080), 60);
        var profile = new Profile(monitor, "Default", new ProfileSetting(0.5, 0.5, 1, 0.5), true, true);
        var otherProfile = new Profile(other, "Default", new ProfileSetting(0.5, 0.5, 1, 0.5), true, true);
        monitor.Profiles.Add(profile);
        other.Profiles.Add(otherProfile);
        computer.Monitors.AddRange(new[] { monitor, other });
        Check(SettingsViewModel.ValidateBrightnessSettings(computer) == null, "Default brightness shortcuts must be valid.");
        computer.BrightnessDecreaseKey = computer.BrightnessIncreaseKey;
        Check(SettingsViewModel.ValidateBrightnessSettings(computer) != null, "Two directions cannot share a shortcut.");
        computer.BrightnessDecreaseKey = Key.PageDown;
        profile.HotkeyKey = computer.BrightnessIncreaseKey;
        profile.HotkeyModifiers = computer.BrightnessIncreaseModifiers;
        Check(BrightnessAutomationController.GetShortcutValidationError(computer, true) != null &&
            ShellViewModel.GetProfileShortcutValidationError(computer, profile) != null, "Brightness/profile conflicts must be reported on both sides.");
        computer.IsBrightnessShortcutsEnabled = false;
        Check(ShellViewModel.GetProfileShortcutValidationError(computer, profile) == null, "Disabled brightness shortcuts must release profile combinations.");
        otherProfile.HotkeyKey = profile.HotkeyKey;
        otherProfile.HotkeyModifiers = profile.HotkeyModifiers;
        Check(ShellViewModel.GetProfileShortcutValidationError(computer, profile) != null, "Profile conflicts must span monitors.");
        otherProfile.HotkeyKey = null;
        profile.HotkeyKey = Key.A;
        profile.HotkeyModifiers = null;
        Check(ShellViewModel.GetProfileShortcutValidationError(computer, profile) != null, "Unmodified letter shortcuts must fail validation.");
        profile.HotkeyKey = null;
        profile.HotkeyModifiers = null;
        computer.BrightnessShortcutStep = 0;
        Check(SettingsViewModel.ValidateBrightnessSettings(computer) != null, "Zero shortcut step must not save.");
        computer.BrightnessShortcutStep = 10;
        computer.IsBrightnessScheduleEnabled = true;
        Check(SettingsViewModel.ValidateBrightnessSettings(computer) != null, "An enabled empty schedule must not save.");
        computer.BrightnessSchedules.Add(morning);
        computer.BrightnessSchedules.Add(new BrightnessSchedule { Time = morning.Time, Brightness = 10 });
        Check(SettingsViewModel.ValidateBrightnessSettings(computer) != null, "Duplicate daily times must fail validation.");
        computer.BrightnessSchedules.RemoveAt(1);
        Check(SettingsViewModel.ValidateBrightnessSettings(computer) == null, "Valid schedules must save without enabling shortcuts.");

        var directory = Path.Combine(AppContext.BaseDirectory, "brightness-features-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var platform = PlatformProvider.Current;
        PlatformProvider.Current = new DefaultPlatformProvider();
        try
        {
            var data = new DataController(directory, Path.Combine(directory, "missing.json"));
            var hardware = new FakeBrightness();
            var first = new FakeDisplay("first");
            var second = new FakeDisplay("second");
            hardware.Levels[first.DevicePath] = 20;
            hardware.Levels[second.DevicePath] = 90;
            var cache = new DisplayCache();
            var logger = LogManager.GetLogger("BrightnessFeatures");
            var persistence = new BrightnessPersistenceController(data, cache, hardware, logger);
            persistence.Attach(computer);
            persistence.IsLinked = true;
            Check(data.Load().Value.IsBrightnessLinked && persistence.IsLinked, "Link preference must round-trip.");
            Check(persistence.GetMonitorName(first) == "Desk", "Brightness rows must use saved custom monitor labels.");
            using var automation = new FakeAutomation(cache, hardware, persistence, logger, new[] { first, second });
            automation.Attach(computer);
            automation.AdjustBrightness(15);
            Check(hardware.Levels["first"] == 35 && hardware.Levels["second"] == 100, "Shortcuts must preserve relative levels and clamp at 100.");
            Check(monitor.LastBrightness == 35 && other.LastBrightness == 100, "Only successful shortcut writes must be remembered.");
            automation.AdjustBrightness(int.MinValue);
            Check(hardware.Levels.Values.All(level => level == 0), "Large negative steps cannot overflow.");
            hardware.FailReads.Add("first");
            var writes = hardware.Writes;
            automation.AdjustBrightness(10);
            Check(hardware.Writes == writes + 1 && hardware.Levels["first"] == 0 && hardware.Levels["second"] == 10,
                "A failed read must skip that monitor instead of guessing its level.");
            Check(automation.Status.Contains("1 failed"), "Automation failures must offer a retry route.");
            hardware.FailReads.Clear();
            automation.Detach();
            writes = hardware.Writes;
            automation.AdjustBrightness(50);
            Check(hardware.Writes == writes, "Detached automation must not write hardware during reset.");

            using (var row = new BrightnessMonitorViewModel(first, hardware, brightnessApplied: persistence.Remember, name: "Desk"))
            {
                hardware.FailWrites = true;
                row.Brightness = 65;
                Pump(() => row.FlushPendingBrightnessAsync());
                Check(row.HasError && row.StatusMessage.Contains("failed") && monitor.LastBrightness == 0,
                    "Failed slider writes must show an error without saving the requested level.");
                hardware.FailWrites = false;
                row.Brightness = 65;
                Pump(() => row.FlushPendingBrightnessAsync());
                Check(!row.HasError && hardware.Levels["first"] == 65 && monitor.LastBrightness == 65,
                    "Moving the slider after failure must apply its requested value without a Retry button.");
                writes = hardware.Writes;
                hardware.FailNextWrites = 1;
                row.Brightness = 70;
                Pump(() => row.FlushPendingBrightnessAsync());
                Check(hardware.Writes == writes + 2 && hardware.Levels["first"] == 70 && !row.HasError,
                    "A transient busy monitor must recover with one automatic retry.");
                using var started = new ManualResetEventSlim();
                using var release = new ManualResetEventSlim();
                hardware.BeforeWrite = () => { started.Set(); if (!release.Wait(5000)) throw new TimeoutException(); };
                writes = hardware.Writes;
                Pump(async () =>
                {
                    row.Brightness = 10;
                    var flush = row.FlushPendingBrightnessAsync();
                    if (!await Task.Run(() => started.Wait(5000))) throw new TimeoutException("Write never started");
                    row.Brightness = 25;
                    row.Brightness = 60;
                    row.Brightness = 95;
                    Check(row.Brightness == 95, "Slow hardware must not block new slider input.");
                    release.Set();
                    await flush;
                });
                hardware.BeforeWrite = null;
                Check(hardware.Writes == writes + 2 && hardware.Levels["first"] == 95 && row.Brightness == 95 && monitor.LastBrightness == 95,
                    "Queued input must coalesce to the latest value and flush it without stale slider feedback.");
                hardware.ExternalStatus(first, new BrightnessStatus("Shortcut", false, 40));
                Check(row.Brightness == 40, "Open rows must follow successful shortcut or schedule writes.");
                row.ApplyBrightness(double.NaN);
                Check(row.Brightness == 40, "Invalid linked slider values must be ignored.");
                hardware.FailReads.Add("first");
                Pump(() => row.RefreshBrightnessAsync());
                Check(row.Brightness == 40 && row.BrightnessText == "40%" && !row.SupportsBrightness,
                    "An unavailable monitor must retain its last known value instead of guessing 50%.");
                using (var sleepingRow = new BrightnessMonitorViewModel(first, hardware, name: "Sleeping"))
                    Check(sleepingRow.Brightness == 40, "Reopening after a failed read must reuse the last successful hardware value.");
                hardware.FailReads.Clear();
                hardware.Levels["first"] = 30;
                Pump(() => row.RefreshBrightnessAsync(new FakeDisplay("first")));
                Check(row.SupportsBrightness && !row.HasError && row.Brightness == 30,
                    "A returning monitor must reconnect its existing slider and replace its stale display object.");
                row.Brightness = 75;
                Pump(() => row.FlushPendingBrightnessAsync());
                Check(hardware.Levels["first"] == 75, "A recovered slider must accept brightness changes again.");
                hardware.NotifyDisplayPower(false);
                Check(!row.SupportsBrightness && row.Brightness == 75, "Display sleep must suspend controls without changing the visible value.");
                hardware.NotifyDisplayPower(true);
                Pump(async () => { await Task.Delay(50); await row.RefreshBrightnessAsync(); });
                Check(row.SupportsBrightness && row.Brightness == 75, "Display wake must automatically recover controls.");
            }
            hardware.FailReads.Add("unknown");
            using (var unknown = new BrightnessMonitorViewModel(new FakeDisplay("unknown"), hardware, name: "Unknown"))
                Check(unknown.BrightnessText == "—" && unknown.Brightness != 50, "A monitor without any known level must show an unknown value, never invent 50%.");
            hardware.FailReads.Clear();
            hardware.StatusChanged += (_, _) => throw new InvalidOperationException("Subscriber failure");
            hardware.ExternalStatus(first, new BrightnessStatus("Saved", false, 40));
            Check(hardware.GetStatus(first)?.Message == "Saved", "A feedback subscriber cannot corrupt recorded hardware status.");

            var settings = new SettingsViewModel(computer, new RegistryController(), data);
            settings.IsBrightnessShortcutsEnabled = true;
            settings.BeginRecording(true);
            Check(settings.RecordKey(Key.F10, ModifierKeys.Control), "Brightness shortcut recorder must capture combinations.");
            settings.BrightnessShortcutStep = 7;
            settings.BrightnessSchedules[0].Brightness = 60;
            Check(!computer.IsBrightnessShortcutsEnabled && computer.BrightnessShortcutStep == 10 && morning.Brightness == 80,
                "Automation edits must stay in a detached draft until explicitly saved.");
            settings.SaveBrightnessAutomation();
            Check(computer.IsBrightnessShortcutsEnabled && computer.BrightnessShortcutStep == 7 && computer.BrightnessIncreaseKey == Key.F10 &&
                data.Load().Value.BrightnessSchedules[0].Brightness == 60, "Saved automation must include keys, steps and schedule drafts.");
            settings.BeginRecording(false);
            settings.RecordKey(Key.Escape, ModifierKeys.None);
            Check(settings.DecreaseShortcutText.Contains("Page Down"), "Cancelling key capture must preserve its combination.");
            settings.BrightnessShortcutStep = 101;
            settings.SaveBrightnessAutomation();
            Check(computer.BrightnessShortcutStep == 7 && settings.SettingsStatus.Contains("between"), "Invalid automation must leave saved settings untouched.");

            var blocked = Path.Combine(directory, "blocked");
            File.WriteAllText(blocked, "sentinel");
            var blockedData = new DataController(blocked, Path.Combine(directory, "missing-legacy.json"));
            var blockedSettings = new SettingsViewModel(computer, new RegistryController(), blockedData);
            blockedSettings.BrightnessShortcutStep = 9;
            blockedSettings.SaveBrightnessAutomation();
            Check(computer.BrightnessShortcutStep == 7 && blockedSettings.SettingsStatus.Contains("Could not save"), "Failed saves must roll back automation fields.");
            blockedSettings.IsStartMinimized = !computer.IsStartMinimized;
            Check(!computer.IsStartMinimized, "Failed option saves must restore their old values.");
            var blockedPersistence = new BrightnessPersistenceController(blockedData, cache, hardware, logger);
            blockedPersistence.Attach(computer);
            try { blockedPersistence.IsLinked = false; throw new InvalidOperationException("Expected failed link save"); }
            catch (IOException) { count++; }
            Check(computer.IsBrightnessLinked, "Failed link preference saves must restore their old values.");
            persistence.Detach();
            persistence.IsLinked = false;
            Check(computer.IsBrightnessLinked, "Detached persistence must not modify the old link preference.");
        }
        finally
        {
            PlatformProvider.Current = platform;
            Directory.Delete(directory, true);
        }
        return count;
    }

    private sealed class FakeBrightness : MonitorBrightnessController
    {
        public FakeBrightness() : base(LogManager.GetLogger("FakeBrightness")) { }
        public readonly Dictionary<string, int> Levels = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> FailReads = new(StringComparer.OrdinalIgnoreCase);
        public bool FailWrites;
        public int FailNextWrites;
        public Action? BeforeWrite;
        public int Writes;
        public override bool SupportsBrightness(Display display) => true;
        public override int? GetBrightness(Display display)
        {
            int? value = FailReads.Contains(display.DevicePath) ? null : Levels[display.DevicePath];
            RecordStatus(display, new BrightnessStatus(value.HasValue ? "Connected" : "Read failed", !value.HasValue, value));
            return value;
        }
        public override bool SetBrightness(Display display, int brightness)
        {
            Writes++;
            BeforeWrite?.Invoke();
            var fail = FailWrites || FailNextWrites-- > 0;
            if (!fail) Levels[display.DevicePath] = brightness;
            RecordStatus(display, new BrightnessStatus(fail ? "Write failed" : "Applied", fail, brightness));
            return !fail;
        }
        public void ExternalStatus(Display display, BrightnessStatus status) => RecordStatus(display, status);
    }

    internal static void Pump(Func<Task> work)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        try
        {
            var task = work();
            var frame = new DispatcherFrame();
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timeout.Tick += (_, _) => frame.Continue = false;
            timeout.Start();
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            if (!task.IsCompleted) Dispatcher.PushFrame(frame);
            timeout.Stop();
            if (!task.IsCompleted) throw new TimeoutException("Brightness worker did not finish.");
            task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private sealed class FakeAutomation : BrightnessAutomationController
    {
        private readonly IReadOnlyList<Display> _displays;
        public FakeAutomation(DisplayCache cache, MonitorBrightnessController hardware, BrightnessPersistenceController persistence,
            ILogger logger, IReadOnlyList<Display> displays) : base(cache, hardware, persistence, logger) => _displays = displays;
        protected override IReadOnlyList<Display> GetConnectedDisplays() => _displays;
    }
    private sealed class FakeDisplay : Display
    {
        public FakeDisplay(string path) : base(new FakeDevice(path)) { }
        public override bool IsAvailable => false;
    }
    private sealed class FakeDevice : DisplayDevice
    {
        public FakeDevice(string path) : base(path, "Regression display", "Regression key") { }
    }
}
