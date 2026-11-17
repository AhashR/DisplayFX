using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caliburn.Micro;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.BrightnessFlyout;
using DisplayFX.Interface.Monitors;
using DisplayFX.Interface.Profiles;
using DisplayFX.Interface.ProfileSettings;
using DisplayFX.Interface.Shell;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.Factories;
using NLog;
using WindowsDisplayAPI;
using M = DisplayFX.Objects.Entities.Monitor;

internal static class FeatureExpansionRegressionChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly ILogger Logger = NLog.LogManager.GetLogger("FeatureExpansion");
    public static int Run()
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        void Reject(System.Action action, string message)
        {
            var rejected = false;
            try { action(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, message);
        }
        var platform = PlatformProvider.Current; PlatformProvider.Current = new DefaultPlatformProvider();
        var directory = Path.Combine(AppContext.BaseDirectory, "features-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new FeatureSettings();
            Check(options.Validate() == null, "Migrated feature preferences must have valid conservative defaults.");
            options.ProfileSets.Add(new() { Name = "bad", Entries = new() { new() { DevicePath = "one", ProfileId = Guid.Empty } } });
            Check(options.Validate() != null, "Empty profile references in a set must be rejected.");

            using var scene = new Scene(directory);
            var settings = scene.Profile.ProfileSettings!;
            settings.BrightnessInput = "75";
            settings.ContrastInput = "bad";
            Check(settings.Brightness == .75 && settings.HasInputErrors && settings.ContrastInput == "bad", "Precise color input must preserve invalid raw drafts and accept valid percentages.");
            Check(!scene.Shell.CanApply, "Invalid numeric color drafts must disable Apply.");
            var writes = 0;
            try { settings.Save(() => writes++); } catch (InvalidOperationException) { }
            Check(writes == 0 && scene.Profile.Profile.ProfileSetting.Brightness == .5, "Invalid drafts must not reach persistence.");
            settings.ContrastInput = "60"; Check(!settings.HasInputErrors && scene.Shell.CanApply, "Correcting a numeric draft must re-enable application.");
            settings.Gamma = 1.5; settings.UndoColor();
            Check(settings.Gamma == 1 && settings.Brightness == .75 && settings.CanRedoColor, "Undo restores the last color change without altering prior edits.");
            settings.RedoColor(); Check(settings.Gamma == 1.5 && settings.Brightness == .75, "Redo restores the color edit.");
            Check(settings.TrySetExactColorValue("Brightness", "12.75%") && settings.Brightness == .1275, "Inline brightness accepts fractional percentages.");
            Check(settings.TrySetExactColorValue("Contrast", "33,5") && settings.Contrast == .335, "Inline contrast accepts a decimal comma.");
            Check(settings.TrySetExactColorValue("DigitalVibrance", "70") && settings.DigitalVibrance == .7, "Inline vibrance uses percentages.");
            Check(settings.TrySetExactColorValue("Gamma", "2.125") && settings.Gamma == 2.125, "Inline gamma keeps exact decimals.");
            Check(!settings.TrySetExactColorValue("Gamma", "3") && settings.Gamma == 2.125 &&
                  !settings.TrySetExactColorValue("Contrast", "NaN") && settings.Contrast == .335, "Invalid input must leave the prior value unchanged.");
            var converter = new DisplayFX.Interface.ProfileSettings.ExactColorValueConverter();
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
            Check(Equals(converter.Convert(1.39633484, typeof(string), "Gamma", culture), "1" + decimalSeparator + "40"),
                "The gamma editor must round its display to two decimal places.");
            foreach (var channel in new[] { "Brightness", "Contrast", "DigitalVibrance" })
                Check(Equals(converter.Convert(.333333333, typeof(string), channel, culture), "33" + decimalSeparator + "33%") &&
                      Equals(converter.Convert(.5, typeof(string), channel, culture), "50%"),
                    "Percentage editors must display at most two decimal places.");
            settings.ResetBrightness(); settings.ResetContrast(); settings.ResetGamma(); settings.ResetVibrance();
            Check(settings.Brightness == .5 && settings.Contrast == .5 && settings.Gamma == 1 && settings.DigitalVibrance == .5, "Independent resets must restore neutral control values.");
            settings.Gamma = 1.5;
            settings.Save(() => scene.Data.Write(scene.Computer));
            var reloaded = scene.Data.Load().Value;
            Check(reloaded.Monitors[0].Profiles[0].ProfileSetting.Gamma == 1.5, "Retained color settings must round-trip through storage.");

            void Metadata(string method, string value) => typeof(ShellViewModel).GetMethod(method, Private)!.Invoke(scene.Shell, new object[] { scene.Profile, value });
            scene.Profile.Profile.Notes = "Film profile"; // Legacy notes remain intact when changing the icon.
            Metadata("SetProfileIcon", "film");
            Check(scene.Data.Load().Value.Monitors[0].Profiles[0].Notes == "Film profile" && scene.Profile.Profile.Icon == "film",
                "Updating the icon must preserve existing profile notes.");
            Metadata("SetProfileIcon", "invalid");
            Check(ProfileIcons.Choices.Count == 10 && ProfileIcons.Choices.Select(icon => icon.Glyph).Distinct().Count() == 10 &&
                scene.Profile.Profile.Icon == "film" && ProfileIcons.Normalize("old custom glyph") == ProfileIcons.DefaultId,
                "The icon picker must offer ten fixed choices and normalize old or unknown glyphs.");
            var linked = (string?)typeof(ProfileSettingViewModel).GetMethod("GetLinkedExecutableDirectory", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { Path.Combine(directory, "game.exe") });
            Check(linked == directory, "The linked executable determines the file picker's starting folder.");
            var upgrade = new DataController(Path.Combine(directory, "upgrade"), directory + ".absent");
            Directory.CreateDirectory(Path.GetDirectoryName(upgrade.DataPath)!);
            var legacy = Newtonsoft.Json.Linq.JObject.FromObject(scene.Computer);
            legacy["IsBrightnessScheduleEnabled"] = true;
            legacy["BrightnessSchedules"] = Newtonsoft.Json.Linq.JArray.Parse("[{\"Time\":\"08:00\",\"Brightness\":45}]");
            File.WriteAllText(upgrade.DataPath, legacy.ToString());
            var migrated = upgrade.Load().Value;
            Check(!migrated.IsBrightnessScheduleEnabled && migrated.BrightnessSchedules.Count == 0,
                "Previously enabled schedules must not run after the upgrade.");
            Check(migrated.Monitors[0].Profiles[0].ProfileSetting.Gamma == 1.5, "Migration keeps profile values.");
            upgrade.Write(migrated);
            Check(Directory.GetFiles(Path.GetDirectoryName(upgrade.DataPath)!).Length == 1, "No automatic backup should be created.");
            var future = Newtonsoft.Json.Linq.JObject.FromObject(scene.Computer); future["DataFormatVersion"] = 3;
            File.WriteAllText(upgrade.DataPath,future.ToString());
            Check(upgrade.Load().IsFailed && Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(upgrade.DataPath)).Value<int>("DataFormatVersion")==3,
                "A future settings format must fail without replacing it with an older backup.");
            var copied = ProfileTransferController.CloneProfile(scene.Profile.Profile, scene.Monitor, "Copy");
            Check(copied.Id != scene.Profile.Profile.Id && copied.ProfileSetting.Gamma == 1.5 && copied.Notes == "Film profile" && copied.LinkedExecutablePath == null,
                "Copying extended profiles must use fresh identity, preserve colors/notes, and detach triggers.");

            checks += RuleChecks();
            checks += ScheduleChecks();
            checks += ProtocolChecks();
            var capabilities = MonitorBrightnessController.ParseVcpCapabilities("(prot(monitor) vcp(10 12 60(01 10 11) D6(01 04 05)))");
            Check(capabilities.Count == 4 && !capabilities.ContainsKey(0x01) && capabilities[0x60].SequenceEqual(new uint[] { 1, 16, 17 }), "VCP option values must not be mistaken for independent advertised features.");
            Check(MonitorBrightnessController.ParseVcpCapabilities("no capabilities").Count == 0, "Absent capability advertisements must not invent monitor controls.");
            var vcp = new FakeVcp();
            var oldVersion = vcp.GetVersion(scene.Display);
            Check(vcp.SetVcpFeature(scene.Display, 0x10, 60) && vcp.GetLastKnownBrightness(scene.Display) == 60 && vcp.GetVersion(scene.Display) > oldVersion,
                "An explicit raw backlight command must update normalized brightness and invalidate older schedule retries.");
            Check(!vcp.SetVcpFeature(scene.Display, 0x10, 101) && !vcp.SetVcpFeature(scene.Display, 0xFF, 1) && vcp.Writes == 1,
                "VCP writes must reject unsupported codes and values outside advertised ranges before calling hardware.");
            Check(WmiInternalPanelBrightness.MatchesInstance(@"\\?\DISPLAY#BOE1234#5&abc&0&UID1#{guid}", @"DISPLAY\BOE1234\5&abc&0&UID1_0"), "Internal panel brightness must match its exact PnP instance.");
            Check(!WmiInternalPanelBrightness.MatchesInstance(@"\\?\DISPLAY#BOE1234#5&abc&0&UID1#{guid}", @"DISPLAY\BOE1234\5&abc&0&UID2_0"), "Similar internal panels must not share brightness commands.");
            var panel = new FakeInternalPanel(); var controller = new MonitorBrightnessController(Logger, panel);
            Check(controller.GetBrightness(scene.Display) == 45 && controller.SetBrightness(scene.Display, 150) && panel.Level == 100, "Internal panel reads and bounded writes must use their own backend.");
            checks += ApplyDraftChecks(directory);
            checks += IndependentBrightnessChecks(directory);
            var neutral = new DisplayGammaRamp(.5, .5, 1);
            var darker = new DisplayGammaRamp(.2, .5, 1);
            DisplayGammaRamp Blend(double fraction) => (DisplayGammaRamp)typeof(DisplayController).GetMethod("BlendRamps", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { neutral, darker, fraction })!;
            Check(Blend(0).Red.SequenceEqual(neutral.Red) && Blend(1).Blue.SequenceEqual(darker.Blue), "Color transition endpoints must match their exact source and target ramps.");
            var imported = new Computer { Monitors = new() { new("fixture", "Desk", new(1920, 1080), 60) } };
            var importProfile = ProfileTransferController.CloneProfile(scene.Profile.Profile, imported.Monitors[0], "Imported");
            imported.Monitors[0].Profiles.Add(importProfile);
            imported.Features.ProfileSets.Add(new() { Name = "Imported set", Entries = new() { new() { DevicePath = "fixture", ProfileId = importProfile.Id } } });
            var merged = ProfileTransferController.MergeSettings(scene.Computer, imported);
            Check(merged.Features.ProfileSets[0].Entries[0].ProfileId == merged.Monitors[0].Profiles.Single(profile => profile.Name == "Imported").Id,
                "Merged profile sets must point at the newly imported identity rather than a discarded source identity.");
            var exported = Path.Combine(directory, "export.json");
            ProfileTransferController.ExportSettings(imported, exported);
            var envelope = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(exported));
            Check(envelope.Value<int>("Version") == 2, "Extended exports must identify format version two.");
            envelope["Version"] = 1; File.WriteAllText(exported, envelope.ToString());
            Check(ProfileTransferController.ReadSettings(exported).Monitors[0].Profiles[0].Name == "Imported", "Legacy version-one export envelopes must remain readable.");
            var single = Path.Combine(directory, "profile.json");
            ProfileTransferController.ExportProfile(scene.Profile.Profile, single);
            var importedProfile = ProfileTransferController.ReadProfile(single, scene.Monitor);
            Check(importedProfile.Id != scene.Profile.Profile.Id && importedProfile.Name != scene.Profile.Name &&
                importedProfile.ProfileSetting.Gamma == 1.5 && importedProfile.Notes == "Film profile",
                "A manual single-profile export must round-trip through import without overwriting an existing profile.");
            Reject(() => ProfileTransferController.ReadProfile(exported, scene.Monitor), "Profile import must reject full settings exports.");
        }
        finally { PlatformProvider.Current = platform; if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return checks;
    }
    private static int RuleChecks()
    {
        var monitor = new M("rule", "Rules", new(1920, 1080), 60);
        var baseline = new Profile(monitor, "Default", new(.5, .5, 1, .5), true, true);
        var game = new Profile(monitor, "Game", new(.5, .5, 1, .5), false) { LinkedExecutablePath = @"C:\Games\game.exe" };
        var reference = new Profile(monitor, "Reference", new(.5, .5, 1, .5), false);
        monitor.Profiles.AddRange(new[] { baseline, game, reference });
        monitor.ReferenceProfileId = reference.Id;
        var switching = new AppProfileSwitchingController();
        Profile? applied = null;
        bool Apply(Profile profile) { applied = profile; foreach (var entry in monitor.Profiles) entry.IsActive = entry == profile; return true; }
        switching.Update(@"C:\Games\game.exe", new[] { monitor }, Apply);
        if (applied != game) throw new InvalidOperationException("One executable link must still activate its profile.");
        switching.Update("editor.exe", new[] { monitor }, Apply);
        if (applied != reference) throw new InvalidOperationException("Leaving a linked app must still restore the configured reference profile.");
        reference.LinkedExecutablePath = @"C:\Games\game.exe";
        if (ProfileTransferController.GetExecutableConflict(game) == null) throw new InvalidOperationException("Duplicate executable links must be reported without priority rules.");
        applied = null;
        switching.Update(@"C:\Games\game.exe", new[] { monitor }, Apply);
        if (applied != null) throw new InvalidOperationException("Old duplicate links must not activate an arbitrary profile.");
        return 4;
    }
    private static int ScheduleChecks()
    {
        var day = new DateTime(2026, 11, 16, 9, 0, 0);
        var schedule = new BrightnessSchedule { Time = "08:00", Brightness = 47 };
        for (var offset = 0; offset < 7; offset++)
        {
            var now = day.AddDays(offset);
            if (BrightnessAutomationController.FindLatestDueSchedule(new[] { schedule }, now.AddHours(-2), now) != schedule)
                throw new InvalidOperationException("Daily brightness must honor the same time on every weekday.");
        }
        if (!BrightnessAutomationController.HasScheduleConflicts(new[] { schedule, schedule.Copy() }))
            throw new InvalidOperationException("Duplicate daily schedules must be reported.");
        return 8;
    }
    private static int ProtocolChecks()
    {
        var command = new LocalCommand("apply", Guid.NewGuid());
        using var stream = new MemoryStream();
        LocalCommandProtocol.Write(stream, command, CancellationToken.None).GetAwaiter().GetResult(); stream.Position = 0;
        if (LocalCommandProtocol.Read<LocalCommand>(stream, 8192, CancellationToken.None).GetAwaiter().GetResult() != command) throw new InvalidOperationException("Local command frames must round-trip.");
        if (LocalCommandProtocol.IsAllowed(new("delete")) || LocalCommandProtocol.IsAllowed(new("apply")) || !LocalCommandProtocol.IsAllowed(new("list"))) throw new InvalidOperationException("The local interface must accept only explicit bounded profile commands.");
        using var invalid = new MemoryStream(BitConverter.GetBytes(8193));
        try { LocalCommandProtocol.Read<LocalCommand>(invalid, 8192, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (InvalidDataException) { return 3; }
        throw new InvalidOperationException("Oversized command frames must be rejected before allocating their payload.");
    }
    private static int ApplyDraftChecks(string directory)
    {
        using var scene = new Scene(Path.Combine(directory, "apply-draft"));
        scene.Profile.ProfileSettings!.Brightness = .7;
        Pump(async () =>
        {
            await scene.Shell.Apply();
            if (scene.Profile.Profile.ProfileSetting.Brightness != .5 || scene.Colors.Applied != .7)
                throw new InvalidOperationException("Applying a draft must use its colors without storing them in the saved profile.");
        });
        return 1;
    }
    private static int IndependentBrightnessChecks(string directory)
    {
        var a = new FakeDisplay("one"); var b = new FakeDisplay("two"); var c = new FakeDisplay("three");
        var monitors = new[] { new M("one", "A", new(1920,1080),60),
            new M("two", "B",new(1920,1080),60), new M("three", "C",new(1920,1080),60) };
        foreach (var monitor in monitors) monitor.Profiles.Add(new(monitor,"Default",new(.5,.5,1,.5),true,true));
        var hardware = new FakeBrightness(); var cache = new FakeCache(a,b,c); var data = new DataController(Path.Combine(directory,"linked"), directory + ".absent");
        var persistence = new BrightnessPersistenceController(data,cache,hardware,Logger); persistence.Attach(new() { Monitors = monitors.ToList() });
        using var flyout = new BrightnessFlyoutViewModel(hardware,cache,persistence,deferInitialization:false);
        persistence.IsLinked = true; // Legacy preferences must not link the tray sliders.
        Pump(async () => { flyout.Monitors[0].Brightness = 70; await flyout.FlushPendingBrightnessAsync(); });
        if (hardware.Levels["one"] != 70 || hardware.GetBrightness(b) != 40 || hardware.GetBrightness(c) != 40)
            throw new InvalidOperationException("Changing one tray slider must not change the other displays, even with a legacy linked preference.");
        Pump(async () => { flyout.Monitors[1].Brightness = 100; await flyout.FlushPendingBrightnessAsync(); });
        if (hardware.Levels["one"] != 70 || hardware.Levels["two"] != 100 || hardware.GetBrightness(c) != 40)
            throw new InvalidOperationException("Every tray slider must stay independent.");
        return 2;
    }
    private static void Pump(Func<Task> action) => BrightnessFeatureRegressionChecks.Pump(action);
    private sealed class Scene : IDisposable
    {
        public ShellViewModel Shell; public M Monitor; public Computer Computer; public ProfileViewModel Profile;
        public FakeDisplay Display = new("fixture"); public FakeColors Colors = new(); public FakeBrightness Brightness = new(); public FakeCache Cache;
        public DataController Data; private readonly SemaphoreSlim _gate = new(1,1);
        public Scene(string directory)
        {
            Monitor = new("fixture","Desk",new(1920,1080),60); var saved = new Profile(Monitor,"Default",new(.5,.5,1,.5),true,true); Monitor.Profiles.Add(saved);
            Computer = new() { Monitors = new() { Monitor } }; var model = new MonitorViewModel(Monitor,Display);
            Profile = new(saved,model,new ProfileSettingViewModelFactory(new EventAggregator())); model.Profiles.Add(Profile);
            Shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
            void Set(string name, object value) => typeof(ShellViewModel).GetField(name,Private)!.SetValue(Shell,value);
            Cache = new(Display); Data = new(directory,directory + ".absent");
            Set("_computer",Computer); Set("_monitors",new ObservableCollection<MonitorViewModel>{model}); Set("_selectedMonitor",model); Set("_selectedProfile",Profile);
            Set("_logger",Logger); Set("_profileRequests",new Dictionary<M,long>()); Set("_colorGate",_gate); Set("_displayController",Colors); Set("_appSwitching",new AppProfileSwitchingController());
            Set("_dataController",Data); Set("_colorDiagnostics",new ColorUpdateDiagnostics(Cache,Brightness,Logger));
        }
        public void Dispose() => _gate.Dispose();
    }
    private sealed class FakeDisplay : Display { public FakeDisplay(string path):base(new FakeDevice(path)){} public override bool IsAvailable=>false; }
    private sealed class FakeDevice : DisplayDevice { public FakeDevice(string path):base(path,"Fixture","Fixture"){} }
    private sealed class FakeCache : DisplayCache { private readonly Display[] _displays; public FakeCache(params Display[] displays)=>_displays=displays; public override void Refresh(){} public override IReadOnlyList<Display> GetDisplays()=>_displays; }
    private sealed class FakeBrightness : MonitorBrightnessController
    {
        public Dictionary<string,int> Levels = new(); public FakeBrightness():base(Logger){}
        public override bool SupportsBrightness(Display display)=>true;
        public override int? GetBrightness(Display display)=>Levels.TryGetValue(display.DevicePath,out var level)?level:40;
        public override bool SetBrightness(Display display,int value) { Levels[display.DevicePath]=value; RecordStatus(display,new("",false,value));return true; }
    }
    private sealed class FakeInternalPanel : IInternalPanelBrightness
    { public int Level=45; public bool TryRead(Display display,out int brightness){brightness=Level;return true;} public bool Supports(Display display)=>true; public bool Set(Display display,int brightness){Level=brightness;return true;} public void Invalidate(){} }
    private sealed class FakeVcp : MonitorBrightnessController
    {
        public int Writes;
        public FakeVcp():base(Logger){}
        public override IReadOnlyList<VcpFeature> ReadVcpFeatures(Display display)=>new[] { new VcpFeature(0x10,"Brightness",40,100,Array.Empty<uint>()) };
        protected override IntPtr GetMonitorHandle(Display display)=>new(1);
        protected override PhysicalMonitor[] GetPhysicalMonitors(IntPtr monitor)=>new[] { new PhysicalMonitor { Handle = new(1), Description = "Fake" } };
        protected override void ReleasePhysicalMonitors(PhysicalMonitor[] monitors){}
        protected override bool WriteVcpFeatureRaw(IntPtr monitor,byte code,uint value){Writes++;return true;}
    }
    private sealed class FakeColors : DisplayController
    {
        public double Applied;
        public FakeColors():base(Logger,null!){}
        public override ColorSnapshot CaptureColors(Display display,NvAPIWrapper.Display.Display? nvidia)=>new(new DisplayGammaRamp(),null);
        public override bool UpdateColorSettings(Display display,ProfileSetting colors,NvAPIWrapper.Display.Display? nvidia){Applied=colors.Brightness;return true;}
    }
}
