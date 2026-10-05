using System.Drawing;
using System.IO;
using Action = System.Action;
using System.Windows.Input;
using Caliburn.Micro;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.ProfileNames;
using DisplayFX.Objects.Entities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Monitor = DisplayFX.Objects.Entities.Monitor;

internal static class ProfileToolsRegressionChecks
{
    internal static int Run()
    {
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        void Reject(Action action, string message)
        {
            try
            {
                action();
                throw new InvalidOperationException(message);
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException) { checks++; }
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "profile-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var monitor = CreateMonitor("connected", "Main", 37);
            var gaming = new Profile(monitor, "Gaming", new ProfileSetting(0.6, 0.7, 1.2, 0.8), false)
            {
                HotkeyKey = Key.F8,
                HotkeyModifiers = ModifierKeys.Control
            };
            monitor.Profiles.Add(gaming);
            var duplicate = ProfileTransferController.CloneProfile(gaming, monitor,
                ProfileTransferController.GetUniqueName(monitor, "Gaming"));
            Check(duplicate.Name == "Gaming (2)" && !duplicate.IsActive && !duplicate.IsDefault &&
                  duplicate.HotkeyKey == null,
                "Duplicates must have unique names and begin inactive without reserving the source triggers.");
            duplicate.ProfileSetting.Brightness = 0.2;
            Check(gaming.ProfileSetting.Brightness == 0.6 && !ReferenceEquals(duplicate.ProfileSetting, gaming.ProfileSetting),
                "Duplicated settings must be independently editable.");
            ProfileTransferController.RenameProfile(gaming, "  Games  ");
            Check(gaming.Name == "Games", "Renames must trim names.");
            Reject(() => ProfileTransferController.RenameProfile(gaming, "default"), "Renames must reject case-insensitive collisions.");
            Reject(() => ProfileTransferController.RenameProfile(gaming, "   "), "Renames must reject empty names.");
            Reject(() => ProfileTransferController.RenameProfile(gaming, new string('x', 81)), "Renames must reject excessively long names.");
            Reject(() => ProfileTransferController.RenameProfile(gaming, "Games\n"), "Renames must reject embedded control characters.");
            Check(gaming.Name == "Games", "Failed rename validation must leave the profile untouched.");

            var profilePath = Path.Combine(directory, "profile.json");
            ProfileTransferController.ExportProfile(gaming, profilePath);
            Check(JObject.Parse(File.ReadAllText(profilePath))["Data"]!["Name"]!.Value<string>() == "Games",
                "Profile export must contain the saved name.");
            JObject malformed;

            var current = new Computer { Monitors = new List<Monitor> { monitor }, IsBrightnessLinked = true };
            monitor.CustomName = "Desk";
            var sourceMonitor = CreateMonitor("connected", "Old hardware name", 99);
            sourceMonitor.Profiles.Add(new Profile(sourceMonitor, "Games", new ProfileSetting(0.2, 0.3, 1.8, 0.4), false)
            {
                HotkeyKey = Key.F9
            });
            var disconnected = CreateMonitor("disconnected", "Spare", 22);
            var imported = new Computer
            {
                Monitors = new List<Monitor> { sourceMonitor, disconnected },
                IsStartMinimized = true,
                IsRememberBrightnessOnStart = true,
                IsBrightnessLinked = false,
                BrightnessShortcutStep = 5,
                BrightnessSchedules = new List<BrightnessSchedule> { new() { Time = "21:30", Brightness = 30 } }
            };
            var settingsPath = Path.Combine(directory, "settings.json");
            ProfileTransferController.ExportSettings(imported, settingsPath);
            var originalExport = File.ReadAllText(settingsPath);
            malformed = JObject.Parse(originalExport);
            malformed["Data"]!["Monitors"]![0]!["Profiles"]![0]!["ProfileSetting"]!["Brightness"] = 1.1;
            File.WriteAllText(settingsPath, malformed.ToString());
            Reject(() => ProfileTransferController.ReadSettings(settingsPath), "Out-of-range colour settings must be rejected.");
            ((JObject)malformed["Data"]!["Monitors"]![0]!["Profiles"]![0]!["ProfileSetting"]!).Remove("Contrast");
            File.WriteAllText(settingsPath, malformed.ToString());
            Reject(() => ProfileTransferController.ReadSettings(settingsPath), "Missing colour parameters must be rejected.");
            malformed = JObject.Parse(originalExport);
            malformed["Version"] = 99;
            File.WriteAllText(settingsPath, malformed.ToString());
            Reject(() => ProfileTransferController.ReadSettings(settingsPath), "Unsupported export versions must be rejected.");
            File.WriteAllText(settingsPath, "{\"Format\":\"DisplayFX.Settings\",\"Format\":\"DisplayFX.Settings\",\"Version\":1,\"Data\":{}}");
            Reject(() => ProfileTransferController.ReadSettings(settingsPath), "Duplicate JSON properties must be rejected.");
            File.WriteAllText(settingsPath, originalExport);
            var parsed = ProfileTransferController.ReadSettings(settingsPath);
            Check(parsed.IsStartMinimized && parsed.BrightnessShortcutStep == 5 && parsed.BrightnessSchedules[0].Time == "21:30" &&
                  parsed.Monitors.All(display => display.Profiles.All(item => ReferenceEquals(item.Monitor, display))),
                "Full settings files must round-trip options, schedules, profiles, and restored monitor ownership.");
            var merged = ProfileTransferController.MergeSettings(current, parsed);
            var mergedMonitor = merged.Monitors[0];
            Check(merged.IsStartMinimized && merged.IsRememberBrightnessOnStart && !merged.IsBrightnessLinked &&
                  mergedMonitor.Name == "Main" && mergedMonitor.CustomName == "Desk" && mergedMonitor.LastBrightness == 37,
                "Settings imports must restore app options while retaining existing monitor identity and saved brightness.");
            Check(mergedMonitor.Profiles.Count == 3 && mergedMonitor.Profiles[0].IsActive && mergedMonitor.Profiles[0].IsDefault &&
                  mergedMonitor.Profiles[1].Name == "Games" && mergedMonitor.Profiles[2].Name == "Games (2)" &&
                  mergedMonitor.Profiles[2].HotkeyKey == Key.F9 && !mergedMonitor.Profiles[2].IsActive && !mergedMonitor.Profiles[2].IsDefault,
                "Settings merge must preserve current profiles and their selection, append renamed collisions, and preserve backed-up triggers.");
            Check(merged.Monitors.Count == 2 && merged.Monitors[1].DisplayDevicePath == "disconnected" &&
                  merged.Monitors[1].LastBrightness == 22 && merged.Monitors[1].Profiles[0].IsDefault,
                "Disconnected imported monitors must remain available for a later reconnect.");
            Check(current.Monitors.Count == 1 && monitor.Profiles.Count == 2 && !current.IsStartMinimized &&
                  sourceMonitor.Profiles[1].Name == "Games" && !ReferenceEquals(mergedMonitor, monitor),
                "Settings merge must leave both source models untouched until persistence succeeds.");
            monitor.Profiles[0].IsActive = false;
            gaming.IsActive = true;
            var liveMerge = ProfileTransferController.MergeSettings(current, parsed);
            Check(liveMerge.Monitors[0].Profiles.First(item => item.IsActive).Name == "Games" &&
                  gaming.IsActive,
                "Merging a backup must preserve the selected active profile and leave its source untouched.");
            monitor.Profiles[0].IsActive = true;
            gaming.IsActive = false;
            mergedMonitor.Profiles[1].ProfileSetting.Gamma = 2;
            Check(gaming.ProfileSetting.Gamma == 1.2, "Merged profiles must be independently editable.");

            var remappedMonitor = CreateMonitor("old-path", "Main", 80);
            remappedMonitor.Profiles.Add(new Profile(remappedMonitor, "Night", new ProfileSetting(0.2, 0.5, 1, 0.5), false));
            var remapped = ProfileTransferController.MergeSettings(current,
                new Computer { Monitors = new List<Monitor> { remappedMonitor } });
            Check(remapped.Monitors.Count == 1 && remapped.Monitors[0].DisplayDevicePath == "connected" &&
                  remapped.Monitors[0].LastBrightness == 37 && remapped.Monitors[0].Profiles.Any(item => item.Name == "Night"),
                "A unique hardware name may map imported profiles to a monitor whose path changed.");
            var ambiguousCurrent = new Computer
            {
                Monitors = new List<Monitor> { CreateMonitor("one", "Identical", 10), CreateMonitor("two", "Identical", 20) }
            };
            var ambiguous = ProfileTransferController.MergeSettings(ambiguousCurrent,
                new Computer { Monitors = new List<Monitor> { CreateMonitor("unknown", "Identical", 30) } });
            Check(ambiguous.Monitors.Count == 3 && ambiguous.Monitors[0].LastBrightness == 10 && ambiguous.Monitors[1].LastBrightness == 20,
                "Ambiguous monitor names must remain disconnected rather than guessing which connected monitor to alter.");

            malformed = JObject.Parse(File.ReadAllText(settingsPath));
            malformed["Data"]!["BrightnessSchedules"]![0]!["Time"] = "24:00";
            File.WriteAllText(settingsPath, malformed.ToString());
            Reject(() => ProfileTransferController.ReadSettings(settingsPath), "Invalid schedules must be rejected before restoring settings.");
            malformed["Data"]!["BrightnessSchedules"]![0]!["Time"] = "21:30";
            malformed["Data"]!["BrightnessShortcutStep"] = 0;
            File.WriteAllText(settingsPath, malformed.ToString());
            Reject(() => ProfileTransferController.ReadSettings(settingsPath), "Invalid brightness shortcut steps must be rejected.");
            ProfileTransferController.ExportSettings(imported, settingsPath);
            malformed = JObject.Parse(File.ReadAllText(settingsPath));
            ((JArray)malformed["Data"]!["Monitors"]!).Add(malformed["Data"]!["Monitors"]![0]!.DeepClone());
            File.WriteAllText(settingsPath, malformed.ToString());
            Reject(() => ProfileTransferController.ReadSettings(settingsPath), "Duplicate monitor paths must be rejected.");
            ProfileTransferController.ExportSettings(imported, settingsPath);
            File.AppendAllText(settingsPath, " {}");
            Reject(() => ProfileTransferController.ReadSettings(settingsPath), "Trailing JSON documents must be rejected.");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Exports must clean temporary files after replacing backups.");
            var full = CreateMonitor("full", "Full", 10);
            for (var index = 1; index < 5; index++)
                full.Profiles.Add(new Profile(full, "Profile " + index, new ProfileSetting(0.5, 0.5, 1, 0.5), false));
            var incoming = CreateMonitor("full", "Full", 30);
            incoming.Profiles.Add(new Profile(incoming, "Incoming", new ProfileSetting(0.7, 0.6, 1, 0.5), false));
            Reject(() => ProfileTransferController.MergeSettings(new Computer { Monitors = new() { full } },
                    new Computer { Monitors = new() { incoming } }), "Imports cannot exceed the five-profile app limit.");
            Check(full.Profiles.Count == 5 && incoming.Profiles.Count == 2,
                "A capacity failure must leave both original configurations untouched.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        var previousPlatform = PlatformProvider.Current;
        PlatformProvider.Current = new DefaultPlatformProvider();
        try
        {
            var naming = new ProfileNameViewModel();
            naming.Configure("Rename profile", "  Night  ", new[] { "Day", "Games" });
            Check(naming.DisplayName == "Rename profile" && naming.CanSave, "Rename dialogs must support initial values and titles.");
            naming.ProfileName = " games ";
            Check(!naming.CanSave && naming.ValidationMessage.Contains("already exists"), "Name dialogs must show monitor-scoped collisions before Save.");
            naming.ProfileName = new string('x', 81);
            Check(!naming.CanSave, "Name dialogs must enforce the same limits as import and rename validation.");
        }
        finally
        {
            PlatformProvider.Current = previousPlatform;
        }
        return checks;
    }

    private static Monitor CreateMonitor(string path, string name, int brightness)
    {
        var monitor = new Monitor(path, name, new Size(1920, 1080), 60) { LastBrightness = brightness };
        monitor.Profiles.Add(new Profile(monitor, "Default", new ProfileSetting(0.5, 0.5, 1, 0.5), true, true));
        return monitor;
    }
}
