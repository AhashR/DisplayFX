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
using DisplayFX.Interface.Workspace;
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
            foreach (var scale in new[] { 100, 125, 150, 200 }) { options.TextScalePercent = scale; Check(options.Validate() == null, "Supported text scales must validate."); }
            options.TextScalePercent = 201; Check(options.Validate() != null, "Unsupported text scale must be rejected."); options.TextScalePercent = 100;
            options.Accent = "blue"; Check(options.Validate() != null, "Color preferences must require bounded RGB values."); options.Accent = "#80B5FA";
            options.SolarLatitude = 52; Check(options.Validate() != null, "A solar location needs both coordinates."); options.SolarLongitude = 5;
            Check(options.Validate() == null, "A manually entered solar location must be valid.");
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
            settings.RedGain = .8; settings.TemperatureKelvin = 5000; settings.UndoColor();
            Check(settings.TemperatureKelvin == 6500 && settings.CanRedoColor, "Undo must retain channel values and the prior temperature.");
            settings.RedoColor(); Check(settings.TemperatureKelvin == 5000 && settings.RedGain == .8, "Redo must restore the complete extended color snapshot.");
            settings.ResetBrightness(); settings.ResetContrast(); settings.ResetGamma(); settings.ResetVibrance();
            Check(settings.Brightness == .5 && settings.Contrast == .5 && settings.Gamma == 1 && settings.DigitalVibrance == .5, "Independent resets must restore neutral control values.");
            settings.Save(() => scene.Data.Write(scene.Computer));
            var reloaded = scene.Data.Load().Value;
            Check(reloaded.Monitors[0].Profiles[0].ProfileSetting.RedGain == .8 && reloaded.Monitors[0].Profiles[0].ProfileSetting.TemperatureKelvin == 5000, "RGB and temperature settings must round-trip through storage.");

            var workspace = new WorkspaceViewModel(scene.Shell, scene.Data, scene.Brightness, scene.Cache);
            Check(!workspace.CanExportReport, "Reports must not export placeholder text before a self-test.");
            Pump(workspace.SelfTest);
            Check(workspace.CanExportReport && !workspace.ReportPreview.Contains("Desk") && !workspace.ReportPreview.Contains("fixture"), "Default support reports must omit monitor names and device paths.");
            workspace.Preferences.Theme = "Light"; workspace.SavePreferences();
            Check(scene.Computer.Features.Theme == "Light", "Appearance preferences must save to the live model.");
            workspace.Preferences.Theme = "Dark";
            Check(scene.Computer.Features.Theme == "Light", "Editing preferences after a save must remain a detached draft.");
            workspace.Metadata.Notes = "Film profile"; workspace.Metadata.Icon = "F"; workspace.Metadata.IsFavorite = true; workspace.SaveMetadata();
            Check(scene.Profile.Profile.Notes == "Film profile" && scene.Profile.Profile.IsFavorite, "Profile metadata must persist and refresh the profile card.");
            workspace.Metadata.MarkerColor = "invalid"; workspace.SaveMetadata();
            Check(scene.Profile.Profile.MarkerColor == "#80B5FA", "Invalid profile metadata must roll back without losing saved colors.");
            workspace.MonitorSettings.Minimum = 30; workspace.MonitorSettings.Maximum = 80; workspace.SaveMonitorSettings();
            Check(scene.Monitor.MinimumBrightness == 30 && scene.Monitor.MaximumBrightness == 80, "Monitor brightness bounds must persist independently.");
            workspace.MonitorSettings.Minimum = 90; workspace.SaveMonitorSettings();
            Check(scene.Monitor.MinimumBrightness == 30, "Inverted brightness bounds must not enter persistent state.");
            workspace.MonitorSettings.Minimum = 30;
            scene.Computer.Features.BackupRetention = 2; scene.Computer.Features.VersionedBackups = false;
            scene.Data.CreateSnapshot(scene.Computer); scene.Data.CreateSnapshot(scene.Computer); scene.Data.CreateSnapshot(scene.Computer);
            Check(scene.Data.GetSnapshots().Length == 2, "Versioned backups must honor their bounded retention.");
            Check(scene.Data.ReadSnapshot(scene.Data.GetSnapshots()[0]).Monitors[0].Profiles[0].Notes == "Film profile", "History snapshots must preserve profile metadata.");
            var upgrade = new DataController(Path.Combine(directory,"upgrade"),directory+".absent");
            Directory.CreateDirectory(Path.GetDirectoryName(upgrade.DataPath)!);
            var oldDocument = Newtonsoft.Json.Linq.JObject.FromObject(scene.Computer); oldDocument.Remove("DataFormatVersion");
            File.WriteAllText(upgrade.DataPath,oldDocument.ToString()); upgrade.Write(scene.Computer);
            var protectedPath = Path.Combine(upgrade.SnapshotDirectory,"upgrade-before-format-2.json");
            upgrade.CreateSnapshot(scene.Computer); upgrade.CreateSnapshot(scene.Computer); upgrade.CreateSnapshot(scene.Computer);
            Check(File.Exists(protectedPath) && Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(protectedPath))["DataFormatVersion"]==null,
                "Pre-upgrade settings must survive rotating snapshot retention for rollback to the prior stable app.");
            var future = Newtonsoft.Json.Linq.JObject.FromObject(scene.Computer); future["DataFormatVersion"] = 3;
            File.WriteAllText(upgrade.DataPath,future.ToString());
            Check(upgrade.Load().IsFailed && Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(upgrade.DataPath)).Value<int>("DataFormatVersion")==3,
                "A future settings format must fail without replacing it with an older backup.");
            Reject(() => scene.Data.ReadSnapshot(Path.Combine(directory, "outside.json")), "Snapshot reading must be restricted to the history directory.");
            var copied = ProfileTransferController.CloneProfile(scene.Profile.Profile, scene.Monitor, "Copy");
            Check(copied.Id != scene.Profile.Profile.Id && copied.ProfileSetting.RedGain == .8 && copied.Notes == "Film profile" && copied.AdditionalExecutablePaths.Count == 0,
                "Copying extended profiles must use fresh identity, preserve colors/notes, and detach triggers.");
            scene.Shell.ProfileSearch = "no-match"; Check(!scene.Shell.VisibleProfiles.Any(), "Profile search must filter without changing the stored list.");
            scene.Shell.ProfileSearch = "Default"; Check(scene.Shell.VisibleProfiles.Single() == scene.Profile, "Profile search must return the matching selected-monitor profile.");

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
            checks += PreviewChecks(directory);
            checks += PreviewCancellationChecks(directory);
            checks += LinkedBrightnessChecks(directory);
            checks += SetRollbackChecks(directory);
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
            var oldProfile = ProfileTransferController.CloneProfile(importProfile, imported.Monitors[0], importProfile.Name);
            oldProfile.ProfileSetting.Gamma = 1.5;
            Check(new ImportChoice { Profile = oldProfile, CurrentProfile = importProfile }.Comparison.Contains("Gamma: 1 → 1.5"),
                "Snapshot comparisons must show actual before/after saved color values, not only profile counts.");
            envelope["Version"] = 1; File.WriteAllText(exported, envelope.ToString());
            Check(ProfileTransferController.ReadSettings(exported).Monitors[0].Profiles[0].Name == "Imported", "Legacy version-one export envelopes must remain readable.");
            var excluded = ProfileTransferController.CloneProfile(importProfile, imported.Monitors[0], "Excluded");
            imported.Monitors[0].Profiles.Add(excluded);
            imported.Features.ProfileSets.Add(new() { Name = "Excluded set", Entries = new() { new() { DevicePath = "fixture", ProfileId = excluded.Id } } });
            typeof(WorkspaceViewModel).GetMethod("LoadImport", Private)!.Invoke(workspace, new object[] { imported });
            workspace.IncludeImportedPreferences = true;
            workspace.ImportChoices.Single(choice => choice.Profile.Id == excluded.Id).Selected = false;
            var selected = (Computer)typeof(WorkspaceViewModel).GetMethod("BuildSelectedImport", Private)!.Invoke(workspace, null)!;
            Check(selected.Monitors[0].Profiles.Count == 1 && selected.Features.ProfileSets.Count == 1, "Selective imports must exclude unselected profiles and their otherwise dangling profile sets.");
            scene.Computer.Features.ProfileSets.Add(new() { Name = "Local set", Entries = new() { new() { DevicePath = "fixture", ProfileId = scene.Profile.Profile.Id } } });
            workspace.IncludeImportedPreferences = false;
            var retained = (Computer)typeof(WorkspaceViewModel).GetMethod("BuildSelectedImport", Private)!.Invoke(workspace, null)!;
            var retainedMerge = ProfileTransferController.MergeSettings(scene.Computer, retained);
            Check(retainedMerge.Features.ProfileSets.Single().Entries.Single().ProfileId == scene.Profile.Profile.Id,
                "Importing colors without preferences must preserve the identities referenced by existing local profile sets.");
            workspace.SelectedVcp = new VcpFeature(0x10, "Brightness", 40, 100, Array.Empty<uint>());
            workspace.VcpValueInput = "invalid"; Pump(workspace.ApplyVcp);
            Check(workspace.Status.Contains("No command was sent"), "Invalid raw VCP input must not silently send a previously parsed command value.");
            Pump(async () => await workspace.DisposeAsync());
        }
        finally { PlatformProvider.Current = platform; if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return checks;
    }
    private static int SetRollbackChecks(string directory)
    {
        using var scene = new Scene(Path.Combine(directory, "sets"));
        var alternate = new Profile(scene.Monitor, "Alternate", new(.7, .5, 1, .5), false);
        scene.Monitor.Profiles.Add(alternate);
        var alternateVm = new ProfileViewModel(alternate, scene.Profile.MonitorViewModel, new ProfileSettingViewModelFactory(new EventAggregator()));
        scene.Profile.MonitorViewModel.Profiles.Add(alternateVm);
        var second = new M("second", "Second", new(1920,1080), 60);
        var secondProfile = new Profile(second, "Default", new(.5,.5,1,.5), true, true); second.Profiles.Add(secondProfile);
        var secondVm = new MonitorViewModel(second, new FakeDisplay("second"));
        secondVm.Profiles.Add(new(secondProfile, secondVm, new ProfileSettingViewModelFactory(new EventAggregator())));
        scene.Computer.Monitors.Add(second); scene.Shell.Monitors.Add(secondVm);
        var set = new ProfileSet { Name = "Both", Entries = new() { new() { DevicePath = "fixture", ProfileId = alternate.Id }, new() { DevicePath = "second", ProfileId = secondProfile.Id } } };
        scene.Colors.FailOnPath = "second";
        var rejected = false;
        Pump(async () => { try { await (Task)typeof(ShellViewModel).GetMethod("ApplySet", Private)!.Invoke(scene.Shell, new object[] { set })!; } catch (InvalidOperationException) { rejected = true; } });
        if (!rejected || scene.Colors.Restores != 2 || !scene.Profile.IsActive || alternateVm.IsActive)
            throw new InvalidOperationException("A partially failed multi-monitor set must restore every attempted display and the previous active profile flags.");
        return 1;
    }
    private static int RuleChecks()
    {
        var monitor = new M("rule", "Rules", new(1920, 1080), 60);
        var baseline = new Profile(monitor, "Default", new(.5, .5, 1, .5), true, true);
        var game = new Profile(monitor, "Game", new(.5, .5, 1, .5), false) { RulePriority = 5, FullscreenOnly = true, PowerCondition = "AC", AdditionalExecutablePaths = new() { @"C:\Games\game.exe" } };
        var lower = new Profile(monitor, "Lower", new(.5, .5, 1, .5), false) { LinkedExecutablePath = @"C:\Games\game.exe", RulePriority = 1 };
        monitor.Profiles.AddRange(new[] { baseline, game, lower });
        var switching = new AppProfileSwitchingController();
        Profile? applied = null;
        Task<bool> Apply(Profile profile) { applied = profile; foreach (var entry in monitor.Profiles) entry.IsActive = entry == profile; return Task.FromResult(true); }
        switching.UpdateAsync(@"C:\Games\game.exe", new[] { monitor }, Apply, false, "AC").GetAwaiter().GetResult();
        if (applied != lower) throw new InvalidOperationException("Fullscreen-only rules must not activate in a window.");
        switching.UpdateAsync(@"C:\Games\game.exe", new[] { monitor }, Apply, true, "AC").GetAwaiter().GetResult();
        if (applied != game) throw new InvalidOperationException("The highest matching rule priority must win when fullscreen becomes active.");
        switching.UpdateAsync(@"C:\Games\game.exe", new[] { monitor }, Apply, true, "Battery").GetAwaiter().GetResult();
        if (applied != lower) throw new InvalidOperationException("A power change must re-evaluate rules even when the foreground process stays the same.");
        return 3;
    }
    private static int ScheduleChecks()
    {
        var monday = new DateTime(2026, 11, 16, 9, 0, 0);
        var schedule = new BrightnessSchedule { Time = "08:00", Days = 2, DevicePath = "one" };
        if (BrightnessAutomationController.FindLatestDueSchedule(new[] { schedule }, monday.AddHours(-2), monday) != schedule ||
            BrightnessAutomationController.FindLatestDueSchedule(new[] { schedule }, monday.AddDays(1).AddHours(-2), monday.AddDays(1)) != null)
            throw new InvalidOperationException("Weekly brightness must honor the selected local weekday.");
        schedule.OffsetInput = "bad";
        if (schedule.IsValid || schedule.OffsetInput != "bad") throw new InvalidOperationException("Invalid schedule offsets must survive virtualization and block saving.");
        schedule.OffsetInput = "30";
        if (!schedule.IsValid || schedule.Copy().OffsetMinutes != 30 || schedule.Copy().Days != 2 || schedule.Copy().DevicePath != "one") throw new InvalidOperationException("Schedule copies must retain target, weekday and solar offset.");
        var sameTime = new BrightnessSchedule { Time = "08:00", Days = 4, DevicePath = "one", OffsetMinutes = 30 };
        if (BrightnessAutomationController.HasScheduleConflicts(new[] { schedule, sameTime })) throw new InvalidOperationException("The same time on disjoint weekdays is valid.");
        sameTime.Days = 2;
        if (!BrightnessAutomationController.HasScheduleConflicts(new[] { schedule, sameTime })) throw new InvalidOperationException("Overlapping identical per-monitor schedules must be rejected.");
        var sunrise = SolarTimes.Calculate(new(2026, 3, 20), 0, 0, true, TimeZoneInfo.Utc);
        var sunset = SolarTimes.Calculate(new(2026, 3, 20), 0, 0, false, TimeZoneInfo.Utc);
        if (!sunrise.HasValue || !sunset.HasValue || sunrise.Value.Hour is < 5 or > 6 || sunset.Value.Hour is < 17 or > 18 || SolarTimes.Calculate(new(2026, 6, 21), 90, 0, true, TimeZoneInfo.Utc).HasValue)
            throw new InvalidOperationException("Solar times must give plausible equatorial events and skip polar days without a sunrise.");
        return 6;
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
    private static int PreviewChecks(string directory)
    {
        using var scene = new Scene(Path.Combine(directory, "preview"));
        scene.Profile.ProfileSettings!.Brightness = .7;
        Pump(async () =>
        {
            await scene.Shell.PreviewProfile();
            if (!scene.Shell.HasPreview || scene.Profile.Profile.ProfileSetting.Brightness != .5 || scene.Colors.Applied != .7) throw new InvalidOperationException("Timed previews must change hardware without saving edited colors.");
            await scene.Shell.CancelPreview();
            if (scene.Shell.HasPreview || scene.Colors.Restores != 1) throw new InvalidOperationException("Cancel must restore the captured colors exactly once.");
            await scene.Shell.PreviewProfile(); await scene.Shell.KeepPreview();
            if (scene.Shell.HasPreview || scene.Profile.Profile.ProfileSetting.Brightness != .5 || scene.Colors.Applied != .7) throw new InvalidOperationException("Keeping a preview must retain the preview colors without implicitly saving the draft.");
        });
        return 3;
    }
    private static int LinkedBrightnessChecks(string directory)
    {
        var a = new FakeDisplay("one"); var b = new FakeDisplay("two"); var c = new FakeDisplay("three");
        var monitors = new[] { new M("one", "A", new(1920,1080),60) { BrightnessGroup = "Desk", BrightnessOffset = 10, MinimumBrightness = 30, MaximumBrightness = 80 },
            new M("two", "B",new(1920,1080),60) { BrightnessGroup = "Desk", BrightnessOffset = -10 }, new M("three", "C",new(1920,1080),60) { BrightnessGroup = "Other" } };
        foreach (var monitor in monitors) monitor.Profiles.Add(new(monitor,"Default",new(.5,.5,1,.5),true,true));
        var hardware = new FakeBrightness(); var cache = new FakeCache(a,b,c); var data = new DataController(Path.Combine(directory,"linked"), directory + ".absent");
        var persistence = new BrightnessPersistenceController(data,cache,hardware,Logger); persistence.Attach(new() { Monitors = monitors.ToList() });
        using var flyout = new BrightnessFlyoutViewModel(hardware,cache,persistence,deferInitialization:false);
        flyout.IsLinked = true;
        Pump(async () => { flyout.Monitors[0].Brightness = 70; await flyout.FlushPendingBrightnessAsync(); });
        if (hardware.Levels["one"] != 70 || hardware.Levels["two"] != 50 || hardware.Levels.ContainsKey("three")) throw new InvalidOperationException("Linked groups must apply offsets without affecting unrelated monitors.");
        Pump(async () => { flyout.Monitors[0].Brightness = 100; await flyout.FlushPendingBrightnessAsync(); });
        if (hardware.Levels["one"] != 80 || hardware.Levels["two"] != 60) throw new InvalidOperationException("Per-monitor brightness bounds must clamp before linked offsets are broadcast.");
        return 2;
    }
    private static int PreviewCancellationChecks(string directory)
    {
        using var scene = new Scene(Path.Combine(directory,"preview-cancel"));
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        scene.Colors.Entered = entered; scene.Colors.Release = release;
        Pump(async () =>
        {
            var preview = scene.Shell.PreviewProfile();
            if (!await Task.Run(()=>entered.Wait(5000))) throw new TimeoutException("Preview did not reach the fake color backend.");
            var cancel = scene.Shell.CancelPreview(); release.Set(); await preview; await cancel;
            if (scene.Shell.HasPreview || scene.Colors.Restores != 1) throw new InvalidOperationException("Cancel during an in-flight preview must restore its captured colors before returning.");
        });
        scene.Colors.Entered = null; scene.Colors.Release = null;
        Pump(async () =>
        {
            await scene.Shell.PreviewProfile();
            var state = typeof(ShellViewModel).GetField("_previewState",Private)!.GetValue(scene.Shell)!;
            state.GetType().GetProperty("Deadline")!.SetValue(state,DateTimeOffset.UtcNow.AddSeconds(-1));
            var until = DateTime.UtcNow.AddSeconds(2);
            while (scene.Shell.HasPreview && DateTime.UtcNow < until) await Task.Delay(20);
            if (scene.Shell.HasPreview || scene.Colors.Restores != 2) throw new InvalidOperationException("An expired preview must automatically restore exactly once.");
        });
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
        public double Applied; public int Restores;
        public string? FailOnPath;
        public ManualResetEventSlim? Entered, Release;
        public FakeColors():base(Logger,null!){}
        public override ColorSnapshot CaptureColors(Display display,NvAPIWrapper.Display.Display? nvidia)=>new(new DisplayGammaRamp(),null);
        public override bool UpdateColorSettings(Display display,ProfileSetting colors,NvAPIWrapper.Display.Display? nvidia){Applied=colors.Brightness;Entered?.Set();if(Release!=null&&!Release.Wait(5000))throw new TimeoutException();return display.DevicePath != FailOnPath;}
        public override bool RestoreColors(Display display,NvAPIWrapper.Display.Display? nvidia,ColorSnapshot snapshot){Restores++;return true;}
    }
}
