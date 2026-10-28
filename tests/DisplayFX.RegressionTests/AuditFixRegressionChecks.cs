using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Windows.Threading;
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
using Action = System.Action;
using M = DisplayFX.Objects.Entities.Monitor;

internal static class AuditFixRegressionChecks
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly ILogger Logger = NLog.LogManager.GetLogger("AuditFixTests");
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); count++; }
        var directory = Path.Combine(AppContext.BaseDirectory, "audit-" + Guid.NewGuid().ToString("N"));
        var platform = PlatformProvider.Current;
        PlatformProvider.Current = new DefaultPlatformProvider();
        try
        {
            var data = new DataController(directory, directory + ".json");
            var monitor = MakeMonitor("one");
            var computer = new Computer { Monitors = new() { monitor } };
            var display = new FixtureDisplay("one");
            var hardware = new FixtureBrightness();
            var cache = new FixtureCache(display);
            var persistence = new BrightnessPersistenceController(data, cache, hardware, Logger);
            persistence.Attach(computer);
            var dispatcher = Dispatcher.CurrentDispatcher;
            using (var row = new BrightnessMonitorViewModel(display, hardware, brightnessApplied: persistence.Remember))
            {
                hardware.AfterWrite = () => dispatcher.Invoke(() => { hardware.External(display, 95); persistence.Remember(display, 95); });
                Pump(() => { row.Brightness = 65; return row.FlushPendingBrightnessAsync(); });
                hardware.AfterWrite = null;
                Check(hardware.Level == 95 && monitor.LastBrightness == 95 && row.Brightness == 95, "A late slider completion must retain newer hardware, persisted, and visible levels.");
            }
            hardware.External(display, 40);
            var diagnostics = new ColorUpdateDiagnostics(cache, hardware, Logger) { Enabled = true };
            var initialWrites = hardware.Writes;
            int reads = 0;
            hardware.AfterRead = () => { if (++reads == 2) hardware.External(display, 100); };
            Pump(() => diagnostics.ApplyAsync(() => true));
            hardware.AfterRead = null;
            Check(diagnostics.Warning != null && hardware.Writes == initialWrites, "Delayed color diagnostics must detect changes without correcting or guessing physical brightness.");
            hardware.External(display, 40);
            using (var row = new BrightnessMonitorViewModel(display, hardware))
            using (var started = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                hardware.AfterRead = () => { started.Set(); if (!release.Wait(5000)) throw new TimeoutException(); };
                Pump(async () =>
                {
                    var refresh = row.RefreshBrightnessAsync();
                    try
                    {
                        if (!await Task.Run(() => started.Wait(5000))) throw new TimeoutException();
                        row.Brightness = 65;
                        await row.FlushPendingBrightnessAsync();
                    }
                    finally { release.Set(); }
                    await refresh;
                });
                hardware.AfterRead = null;
                Check(hardware.Level == 65 && row.Brightness == 65, "An older read must not roll back newer slider state.");
            }
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                hardware.AfterRead = () => { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); };
                BrightnessMonitorViewModel? row = null;
                Pump(async () =>
                {
                    row = new BrightnessMonitorViewModel(display, hardware, name: "Desk", deferInitialization: true);
                    try
                    {
                        if (!await Task.Run(() => entered.Wait(5000))) throw new TimeoutException();
                        var responsive = false;
                        await dispatcher.InvokeAsync(() => responsive = true);
                        Check(responsive, "Initializing brightness controls must not block the dispatcher.");
                    }
                    finally { release.Set(); }
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                });
                hardware.AfterRead = null;
                row?.Dispose();
            }
            var switching = new AppProfileSwitchingController();
            var manual = monitor.Profiles[0];
            var game = new Profile(monitor, "Game", new(.6, .5, 1, .5), false) { LinkedExecutablePath = "game.exe" };
            monitor.Profiles.Add(game);
            int applies = 0;
            bool Apply(Profile target) { applies++; foreach (var profile in monitor.Profiles) profile.IsActive = ReferenceEquals(target, profile); return true; }
            switching.Update("game.exe", computer.Monitors, Apply);
            switching.InvalidateHardware(computer.Monitors);
            switching.Update("game.exe", computer.Monitors, Apply);
            Check(applies == 2 && game.IsActive, "An invalidated active profile must be reapplied after reconnect.");
            Check(manual.IsActiveBeforeAutomaticSwitch == true, "Reapplying an automatic profile must retain its original baseline.");
            manual.LinkedExecutablePath = "game.exe";
            Check(ProfileTransferController.GetExecutableConflict(game) != null, "Duplicate executable links must be reported.");
            switching.Refresh(); switching.Update("game.exe", computer.Monitors, Apply);
            Check(manual.IsActive, "Ambiguous executable links must restore the manual baseline instead of choosing a winner.");
            manual.LinkedExecutablePath = null;
            var export = Path.Combine(directory, "profile.json");
            ProfileTransferController.ExportProfile(game, export);
            var imported = ProfileTransferController.ReadProfile(export, monitor);
            Check(imported.Name == "Game (2)" && imported.LinkedExecutablePath == null && imported.ProfileSetting.Brightness == .6, "Profile exports must round-trip with detached colors and safe triggers.");

            var invalidData = new DataController(Path.Combine(directory, "invalid"), directory + ".missing");
            Directory.CreateDirectory(Path.GetDirectoryName(invalidData.DataPath)!);
            File.WriteAllText(invalidData.DataPath, "{\"Monitors\":[{\"DisplayDevicePath\":\"one\",\"Name\":\"One\",\"Profiles\":[],\"LastBrightness\":1000}]}");
            Check(invalidData.Load().IsFailed, "Invalid stored monitor capacities and brightness must be rejected before use.");
            monitor.Profiles.Remove(game); manual.IsActive = true; manual.IsActiveBeforeAutomaticSwitch = null;
            computer.IsBrightnessScheduleEnabled = true;
            computer.BrightnessSchedules = new() { new() { Time = "08:00", Brightness = 80 } };
            using (var automation = new FixtureAutomation(cache, hardware, persistence))
            {
                automation.Attach(computer); automation.Now = new(2026, 10, 29, 7, 59, 0); automation.Start();
                hardware.FailWrites = true;
                Pump(async () => { automation.Now = new(2026, 10, 29, 8, 1, 0); automation.Tick(); await automation.Work; });
                hardware.FailWrites = false;
                Pump(async () => { automation.Now = new(2026, 10, 29, 8, 2, 0); automation.Tick(); await automation.Work; });
                Check(hardware.Level == 80 && monitor.LastBrightness == 80, "A temporarily failed schedule must retry only failed outputs after recovery.");
            }
            var draft = new BrightnessSchedule { Time = "08:00", BrightnessInput = "bad" };
            Check(!draft.IsValid && draft.BrightnessInput == "bad", "Numeric draft validation must live in the model and survive page destruction.");
            count += NativeLockChecks();
            count += IdentityChecks();
            count += ProfileApplicationChecks(directory);
            var logging = NLog.LogManager.Configuration;
            try
            {
                LoggingController.Configure(Path.Combine(directory, "logs"));
                Logger.Warn("Audit logging verification");
                NLog.LogManager.Flush();
                Check(File.ReadAllText(Path.Combine(directory, "logs", "DisplayFX.log")).Contains("Audit logging verification"), "Diagnostics must have a durable bounded file target.");
            }
            finally { NLog.LogManager.Configuration = logging; }
            Check(!typeof(DisplayController).GetFields(Instance).Any(field => field.FieldType == typeof(MonitorBrightnessController)), "GPU profile application must have no physical-brightness writer dependency.");
        }
        finally { PlatformProvider.Current = platform; if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return count;
    }

    private static int NativeLockChecks()
    {
        var native = new NativeFixture();
        var display = new FixtureDisplay("native");
        var channels = (System.Collections.IDictionary)typeof(MonitorBrightnessController).GetField("_channels", Instance)!.GetValue(native)!;
        native.GetBrightness(display);
        var channel = channels[display.DevicePath]!;
        using var started = new ManualResetEventSlim();
        Task<bool> waiting;
        lock (channel)
        {
            waiting = Task.Run(() => { started.Set(); return native.SetBrightness(display, 65); });
            started.Wait();
            native.NotifyDisplayPower(false);
        }
        if (waiting.GetAwaiter().GetResult() || native.Writes != 0) throw new InvalidOperationException("Waiting writes must recheck sleep inside the hardware lock.");
        native.NotifyDisplayPower(true);
        native.Count = 2;
        if (native.GetBrightness(display) != null || native.SetBrightness(display, 65)) throw new InvalidOperationException("Ambiguous cloned outputs must never receive an independent brightness write.");
        native.Count = 1;
        var oldVersion = native.GetVersion(display);
        native.SetBrightness(display, 80);
        if (native.SetBrightnessIfCurrent(display, 20, oldVersion)) throw new InvalidOperationException("Old schedule retries must not override explicit writes.");
        native.Level = 40;
        if (native.AdjustBrightness(display, 5) != 45 || native.Level != 45) throw new InvalidOperationException("Relative changes must read and write within one monitor operation.");
        return 4;
    }

    private static int IdentityChecks()
    {
        var edid = new byte[128]; new byte[] {0,255,255,255,255,255,255,0}.CopyTo(edid,0);
        edid[8] = 12; edid[10] = 34; edid[12] = 77;
        edid[127] = (byte)((256 - edid.Take(127).Sum(value => (int)value) % 256) % 256);
        var identity = MonitorIdentityController.FromEdid(edid);
        if (identity == null) throw new InvalidOperationException("Valid serialized EDID identities must be recognized.");
        edid[127] ^= 1;
        if (MonitorIdentityController.FromEdid(edid) != null) throw new InvalidOperationException("Damaged EDID must not identify another monitor.");
        var saved = new Computer { Monitors = new() { MakeMonitor("old") } };
        saved.Monitors[0].HardwareIdentity = identity;
        var detected = new Computer { Monitors = new() { MakeMonitor("new") } };
        detected.Monitors[0].HardwareIdentity = identity;
        typeof(ComputerFactory).GetMethod("MergeConnectedMonitors", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] {saved, detected});
        if (saved.Monitors.Count != 1 || saved.Monitors[0].DisplayDevicePath != "new") throw new InvalidOperationException("A unique serial must preserve profiles across changed ports.");
        return 3;
    }

    private static int ProfileApplicationChecks(string directory)
    {
        var monitor = MakeMonitor("profile");
        var computer = new Computer { Monitors = new() { monitor } };
        var model = new MonitorViewModel(monitor, new FixtureDisplay("profile"));
        var profile = new ProfileViewModel(monitor.Profiles[0], model, new ProfileSettingViewModelFactory(new EventAggregator()));
        model.Profiles.Add(profile);
        var shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
        void Set(string name, object value) => typeof(ShellViewModel).GetField(name, Instance)!.SetValue(shell, value);
        var colors = new FixtureColors();
        Set("_computer", computer); Set("_monitors", new ObservableCollection<MonitorViewModel> {model});
        Set("_profileRequests", new Dictionary<M, long>()); Set("_colorGate", new SemaphoreSlim(1,1));
        Set("_displayController", colors); Set("_logger", Logger); Set("_appSwitching", new AppProfileSwitchingController());
        Set("_dataController", new DataController(Path.Combine(directory,"profile-apply"), directory + ".absent"));
        Set("_colorDiagnostics", new ColorUpdateDiagnostics(new FixtureCache(), new FixtureBrightness(), Logger));
        var method = typeof(ShellViewModel).GetMethod("ApplyProfile", Instance)!;
        Pump(async () =>
        {
            var work = (Task<bool>)method.Invoke(shell, new object?[] {profile, false, null})!;
            try
            {
                if (!await Task.Run(() => colors.Entered.Wait(5000))) throw new TimeoutException();
                if (work.IsCompleted) throw new InvalidOperationException("GPU work must be asynchronous.");
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => { });
            }
            finally { colors.Release.Set(); }
            if (!await work) throw new InvalidOperationException("Successful background GPU application must update the profile.");
        });
        return 2;
    }

    private static M MakeMonitor(string path)
    { var m = new M(path, "Desk", new(1920,1080),60); m.Profiles.Add(new Profile(m,"Default",new(.5,.5,1,.5),true,true)); return m; }
    private static void Pump(Func<Task> work) => BrightnessFeatureRegressionChecks.Pump(work);
    private sealed class FixtureDisplay : Display { public FixtureDisplay(string path):base(new FixtureDevice(path)){} public override bool IsAvailable => false; }
    private sealed class FixtureDevice : DisplayDevice { public FixtureDevice(string path):base(path,"Fixture","Fixture"){} }
    private sealed class FixtureCache : DisplayCache
    { private readonly Display[] _displays; public FixtureCache(params Display[] displays)=>_displays=displays; public override void Refresh(){} public override IReadOnlyList<Display> GetDisplays()=>_displays; }
    private sealed class FixtureBrightness : MonitorBrightnessController
    {
        public FixtureBrightness():base(Logger){} public int Level=40, Writes; public bool FailWrites; public Action? AfterWrite, AfterRead;
        public override bool SupportsBrightness(Display display)=>true;
        public override int? GetBrightness(Display display) {var level=Level; RecordStatus(display,new("",false,level));AfterRead?.Invoke();return level;}
        public override bool SetBrightness(Display display,int value) {Writes++;if(FailWrites)return false;External(display,value);AfterWrite?.Invoke();return true;}
        public void External(Display display,int value){Level=value;RecordStatus(display,new("",false,value));}
    }
    private sealed class NativeFixture : MonitorBrightnessController
    {
        public NativeFixture():base(Logger){} public int Count=1,Writes; public uint Level=40;
        protected override IntPtr GetMonitorHandle(Display display)=>new(1);
        protected override PhysicalMonitor[] GetPhysicalMonitors(IntPtr handle)=>Enumerable.Range(0,Count).Select(index=>new PhysicalMonitor{Handle=new(index+1),Description="Fake"}).ToArray();
        protected override void ReleasePhysicalMonitors(PhysicalMonitor[] monitors){}
        protected override bool ReadHighLevel(IntPtr handle,out uint min,out uint current,out uint max){min=0;current=Level;max=100;return true;}
        protected override bool WriteHighLevel(IntPtr handle,uint value){Writes++;Level=value;return true;}
    }
    private sealed class FixtureAutomation : BrightnessAutomationController
    {
        public FixtureAutomation(DisplayCache cache,MonitorBrightnessController hardware,BrightnessPersistenceController persistence):base(cache,hardware,persistence,Logger){}
        public DateTime Now; protected override DateTime LocalNow=>Now;
        public void Tick()=>typeof(BrightnessAutomationController).GetMethod("OnTimerTick",Instance)!.Invoke(this,new object?[]{null,EventArgs.Empty});
        public Task Work=>(Task)typeof(BrightnessAutomationController).GetField("_worker",Instance)!.GetValue(this)!;
        protected override void RemoveHotkeys(){}
    }
    private sealed class FixtureColors : DisplayController
    {
        public readonly ManualResetEventSlim Entered=new(),Release=new();
        public FixtureColors():base(Logger,null!){}
        public override bool UpdateColorSettings(Display display,ProfileSetting settings,NvAPIWrapper.Display.Display? nvidia)
        { Entered.Set(); if(!Release.Wait(5000))throw new TimeoutException();return true; }
    }
}
