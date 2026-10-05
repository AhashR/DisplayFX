using System.Reflection;
using System.IO;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.Shell;
using DisplayFX.Objects.Entities;
using Monitor = DisplayFX.Objects.Entities.Monitor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--diagnose-brightness")
                return BrightnessDiagnostics.Run();
            var count = StorageChecks() + TrayChecks() + ColorTransactionChecks();
            count += BrightnessRegressionChecks.Run();
            count += ProfileRegressionChecks.Run();
            count += AppProfileRegressionChecks.Run();
            count += LibraryRegressionChecks.Run();
            count += RestartRegressionChecks.Run();
            count += ProfileToolsRegressionChecks.Run();
            count += MonitorFeatureRegressionChecks.Run();
            count += BrightnessFeatureRegressionChecks.Run();
            count += UiSmokeChecks.Run(args.Length == 2 && args[0] == "--render-ui" ? args[1] : null);
            Console.WriteLine($"Passed {count} hardware-independent regression checks.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int StorageChecks()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtures);
        var count = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            count++;
        }

        try
        {
            var legacyPath = Path.Combine(fixtures, "legacy.json");
            var data = new DataController(Path.Combine(fixtures, "new-directory"), legacyPath);
            Check(data.Load().IsFailed && !data.HasSavedData, "Missing data must permit first-run initialization.");
            File.WriteAllText(legacyPath, "");
            Check(!data.HasSavedData, "Empty shipped template must not count as saved settings.");

            var computer = CreateComputer();
            computer.IsRememberBrightnessOnStart = true;
            computer.Monitors[0].LastBrightness = 37;
            data.Write(computer);
            var loaded = data.Load();
            Check(loaded.IsSuccess, "Settings must round-trip into a newly created directory.");
            Check(loaded.Value.IsRememberBrightnessOnStart && loaded.Value.Monitors[0].LastBrightness == 37,
                "Brightness opt-in and saved level must survive round-trip.");
            Check(loaded.Value.Monitors[0].Profiles.All(p => ReferenceEquals(p.Monitor, loaded.Value.Monitors[0])),
                "Every deserialized profile must point to its parent monitor.");
            Check(!File.ReadAllText(data.DataPath).Contains("\"Monitor\":"), "Parent references must not be serialized recursively.");

            computer.IsStartMinimized = true;
            data.Write(computer);
            Check(File.Exists(data.BackupPath), "Replacing settings must retain the previous save.");
            File.WriteAllText(data.DataPath, "{truncated");
            var recovered = data.Load();
            Check(recovered.IsSuccess && !recovered.Value.IsStartMinimized, "Corrupt settings must recover the previous save.");
            Check(Directory.GetFiles(Path.GetDirectoryName(data.DataPath)!, "*.corrupt-*").Length == 1,
                "Recovery must preserve the damaged file.");
            Check(data.Load().IsSuccess && File.Exists(data.BackupPath), "Recovery must leave usable primary and backup files.");
            Check(Directory.GetFiles(Path.GetDirectoryName(data.DataPath)!, "*.tmp").Length == 0,
                "Atomic saves must clean temporary files.");

            var broken = new DataController(Path.Combine(fixtures, "broken"), legacyPath);
            Directory.CreateDirectory(Path.GetDirectoryName(broken.DataPath)!);
            File.WriteAllText(broken.DataPath, "null");
            Check(broken.Load().IsFailed && broken.HasSavedData && File.ReadAllText(broken.DataPath) == "null",
                "Corruption without a backup must fail without overwriting user data.");
            File.WriteAllText(broken.DataPath, "{\"Monitors\":null}");
            Check(broken.Load().IsFailed, "Null monitor list must be rejected.");
            File.WriteAllText(broken.DataPath, "{\"Monitors\":[null]}");
            Check(broken.Load().IsFailed, "Null monitor entries must be rejected.");

            File.Copy(data.DataPath, legacyPath, true);
            var originalLegacy = File.ReadAllText(legacyPath);
            var migrated = new DataController(Path.Combine(fixtures, "migrated"), legacyPath);
            Check(migrated.Load().IsSuccess && File.Exists(migrated.DataPath), "Legacy settings must import on first launch.");
            Check(File.ReadAllText(legacyPath) == originalLegacy, "Migration must preserve the original legacy file.");
            File.WriteAllText(legacyPath, "{broken legacy");
            Check(migrated.Load().IsSuccess, "An existing per-user save must take precedence over legacy data.");
            var badMigration = new DataController(Path.Combine(fixtures, "bad-migration"), legacyPath);
            Check(badMigration.Load().IsFailed && badMigration.HasSavedData && !File.Exists(badMigration.DataPath),
                "Corrupt legacy settings must not be silently replaced with defaults.");

            File.Delete(data.DataPath);
            Check(data.Load().IsSuccess && File.Exists(data.DataPath), "Missing primary settings must recover a valid backup.");

            // Old releases wrote null parent references and sometimes left no active profile.
            File.WriteAllText(broken.DataPath,
                "{\"Monitors\":[{\"DisplayDevicePath\":\"test\",\"Name\":\"Legacy\",\"Profiles\":[" +
                "{\"Monitor\":null,\"Name\":\"Default\",\"ProfileSetting\":{\"Brightness\":0.5,\"Contrast\":0.5,\"Gamma\":1,\"DigitalVibrance\":0.5}}]}]}");
            var repaired = broken.Load();
            Check(repaired.IsSuccess && repaired.Value.Monitors[0].Profiles[0].IsActive &&
                  repaired.Value.Monitors[0].Profiles[0].IsDefault &&
                  ReferenceEquals(repaired.Value.Monitors[0], repaired.Value.Monitors[0].Profiles[0].Monitor),
                "Legacy parent references and missing active/default flags must be repaired.");

            var baseline = CreateComputer();
            var baselineMonitor = baseline.Monitors[0];
            baselineMonitor.Profiles[0].IsActive = false;
            baselineMonitor.Profiles.Add(new Profile(baselineMonitor, "Temporary app profile",
                new ProfileSetting(0.5, 0.5, 1, 0.5), true));
            var legacyDocument = Newtonsoft.Json.Linq.JObject.FromObject(baseline);
            legacyDocument["Monitors"]![0]!["Profiles"]![0]!["IsActiveBeforeAutomaticSwitch"] = true;
            legacyDocument["Monitors"]![0]!["Profiles"]![1]!["IsActiveBeforeAutomaticSwitch"] = false;
            legacyDocument["Monitors"]![0]!["Profiles"]![1]!["LinkedExecutablePath"] = "legacy.exe";
            legacyDocument["IsAutomaticSwitchingPaused"] = true;
            File.WriteAllText(broken.DataPath, legacyDocument.ToString());
            var restoredBaseline = broken.Load();
            Check(restoredBaseline.IsSuccess && restoredBaseline.Value.Monitors[0].Profiles[0].IsActive &&
                !restoredBaseline.Value.Monitors[0].Profiles[1].IsActive &&
                !Newtonsoft.Json.JsonConvert.SerializeObject(restoredBaseline.Value).Contains("IsActiveBeforeAutomaticSwitch") &&
                restoredBaseline.Value.Monitors[0].Profiles[1].LinkedExecutablePath == "legacy.exe" &&
                !Newtonsoft.Json.JsonConvert.SerializeObject(restoredBaseline.Value).Contains("IsAutomaticSwitchingPaused"),
                "Old settings must restore the manual profile, preserve executable links and discard the obsolete pause option.");
        }
        finally
        {
            Directory.Delete(fixtures, true);
        }

        return count;
    }

    private static Computer CreateComputer()
    {
        var monitor = new Monitor("test-device", "Test monitor", new System.Drawing.Size(1920, 1080), 60);
        monitor.Profiles.Add(new Profile(monitor, "Default", new ProfileSetting(0.5, 0.5, 1, 0.5), true, true));
        return new Computer { Monitors = new List<Monitor> { monitor } };
    }

    private static int TrayChecks()
    {
        var createText = typeof(ShellView).GetMethod("CreateToolTipText", BindingFlags.NonPublic | BindingFlags.Static)!;
        var computer = CreateComputer();
        computer.Monitors[0].Profiles[0].Name = new string('x', 300);
        var text = (string)createText.Invoke(null, new object[] { computer })!;
        if (text.Length > 127 || !text.EndsWith("..."))
            throw new InvalidOperationException("Long tray tooltips must be truncated to the Windows limit.");
        computer.Monitors[0].Profiles[0].IsActive = false;
        text = (string)createText.Invoke(null, new object[] { computer })!;
        if (!text.Contains("No active profile"))
            throw new InvalidOperationException("A missing active profile must not break the tray tooltip.");
        return 2;
    }

    private static int ColorTransactionChecks()
    {
        var apply = typeof(DisplayController).GetMethod("ApplyWithRollback", BindingFlags.Static | BindingFlags.NonPublic)!;
        var calls = new List<string>();
        void Invoke(Action gamma, Action? vibrance, Action? restoreGamma, Action? restoreVibrance) =>
            apply.Invoke(null, new object?[] { gamma, vibrance, restoreGamma, restoreVibrance });

        Invoke(() => calls.Add("gamma"), () => calls.Add("vibrance"),
            () => calls.Add("restore-gamma"), () => calls.Add("restore-vibrance"));
        if (!calls.SequenceEqual(new[] { "gamma", "vibrance" }))
            throw new InvalidOperationException("Successful color changes must not invoke rollback.");
        calls.Clear();
        try
        {
            Invoke(() => calls.Add("gamma"), () => throw new InvalidOperationException("GPU failed"),
                () => calls.Add("restore-gamma"), () => calls.Add("restore-vibrance"));
            throw new Exception("Failed GPU changes must propagate failure.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException?.Message == "GPU failed") { }
        if (!calls.SequenceEqual(new[] { "gamma", "restore-gamma", "restore-vibrance" }))
            throw new InvalidOperationException("A failed combined color update must restore previous values.");
        try
        {
            Invoke(() => throw new InvalidOperationException("Original failure"), null,
                () => throw new Exception("Rollback failed"), null);
            throw new Exception("Failed gamma changes must propagate failure.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException?.Message == "Original failure") { }
        return 3;
    }
}
