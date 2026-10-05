using System;
using System.Drawing;
using System.Reflection;
using Caliburn.Micro;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.Monitors;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.Factories;
using Newtonsoft.Json;
using WindowsDisplayAPI;
using Monitor = DisplayFX.Objects.Entities.Monitor;

internal static class MonitorFeatureRegressionChecks
{
    internal static int Run()
    {
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }

        var monitor = new Monitor("saved-display", "Old hardware name", new Size(1920, 1080), 60)
        {
            CustomName = " Desk monitor ", LastBrightness = 42
        };
        var profile = new Profile(monitor, "My profile", new ProfileSetting(0.7, 0.6, 1.2, 0.4), true, true);
        monitor.Profiles.Add(profile);
        var disconnected = new Monitor("disconnected-display", "Travel monitor", new Size(1920, 1080), 60);
        var saved = new Computer();
        saved.Monitors.Add(monitor);
        saved.Monitors.Add(disconnected);
        var detected = new Computer();
        detected.Monitors.Add(new Monitor("SAVED-DISPLAY", "New hardware name", new Size(2560, 1440), 144));
        detected.Monitors.Add(new Monitor("saved-display", "Duplicate detection", new Size(800, 600), 30));
        var merge = typeof(ComputerFactory).GetMethod("MergeConnectedMonitors", BindingFlags.NonPublic | BindingFlags.Static)!;

        Check((bool)merge.Invoke(null, new object[] { saved, detected })!,
            "A connected monitor's changed resolution and name must request persistence.");
        Check(ReferenceEquals(saved.Monitors[0], monitor) && monitor.Name == "New hardware name" &&
              monitor.Resolution == new Size(2560, 1440) && monitor.Frequency == 144,
            "Detection must refresh metadata in the existing saved model and ignore duplicate paths.");
        Check(monitor.DisplayName == "Desk monitor" && monitor.CustomName == " Desk monitor " && monitor.LastBrightness == 42,
            "Detection must preserve custom names and remembered brightness.");
        Check(monitor.Profiles.Count == 1 && ReferenceEquals(monitor.Profiles[0], profile) && ReferenceEquals(profile.Monitor, monitor),
            "Detection must preserve profile objects and their parent references.");
        Check(saved.Monitors.Count == 2 && ReferenceEquals(saved.Monitors[1], disconnected),
            "Disconnected saved monitors must survive refresh for future reconnection.");
        Check(!(bool)merge.Invoke(null, new object[] { saved, detected })!,
            "Repeating a topology refresh must not create another metadata change.");
        Check(!(bool)merge.Invoke(null, new object[] { saved, new Computer() })! && saved.Monitors.Count == 2,
            "An empty connected topology must preserve all saved monitor configurations.");

        var serialized = JsonConvert.SerializeObject(monitor);
        var loaded = JsonConvert.DeserializeObject<Monitor>(serialized)!;
        Check(loaded.CustomName == monitor.CustomName && loaded.DisplayName == "Desk monitor" &&
              loaded.Resolution == new Size(2560, 1440) && loaded.Frequency == 144,
            "Labels and refreshed resolution must survive serialization.");
        Check(!serialized.Contains("\"DisplayName\""), "The derived visible label must not create a second saved name.");
        var legacy = JsonConvert.DeserializeObject<Monitor>(
            "{\"DisplayDevicePath\":\"legacy\",\"Name\":\"Legacy monitor\",\"Resolution\":\"1920, 1080\",\"Frequency\":60}")!;
        Check(legacy.CustomName == null && legacy.DisplayName == "Legacy monitor",
            "Older settings without a custom-name field must continue using the hardware name.");

        var previousPlatformProvider = PlatformProvider.Current;
        PlatformProvider.Current = new DefaultPlatformProvider();
        try
        {
            var viewModel = new MonitorViewModel(monitor, null!);
            var writeCount = 0;
            viewModel.CustomNameChanged = _ => writeCount++;
            viewModel.CustomNameDraft = "  Work display  ";
            Check(monitor.DisplayName == "Desk monitor", "Typing a custom name must keep the persisted model unchanged.");
            viewModel.ApplyCustomName();
            Check(monitor.CustomName == "Work display" && viewModel.Name == "Work display" && writeCount == 1,
                "Saving a label must normalize it and invoke persistence.");
            viewModel.CustomNameChanged = _ => throw new InvalidOperationException("Storage unavailable");
            viewModel.CustomNameDraft = "Unsaved name";
            viewModel.ApplyCustomName();
            Check(monitor.CustomName == "Work display" && viewModel.LabelError != null && viewModel.CustomNameDraft == "Unsaved name",
                "A failed label save must roll back the persisted name and retain the draft with an error.");
            viewModel.CustomNameChanged = _ => writeCount++;
            viewModel.ClearCustomName();
            Check(monitor.CustomName == null && viewModel.Name == "New hardware name" && writeCount == 2,
                "Resetting a label must restore the hardware name and persist it.");
            var originalProfiles = viewModel.Profiles;
            var replacement = new FakeDisplay("saved-display");
            viewModel.RefreshDisplay(replacement);
            Check(ReferenceEquals(viewModel.Display, replacement) && ReferenceEquals(viewModel.Profiles, originalProfiles),
                "Refreshing a display reference must preserve the monitor view model and editable profiles.");
            viewModel.CustomNameDraft = new string('x', 81);
            viewModel.ApplyCustomName();
            Check(monitor.CustomName == null && viewModel.LabelError != null && writeCount == 2,
                "Invalid label drafts must never reach persistence.");
        }
        finally
        {
            PlatformProvider.Current = previousPlatformProvider;
        }

        var getNumber = typeof(MonitorIdentificationService).GetMethod("GetDisplayNumber", BindingFlags.NonPublic | BindingFlags.Static)!;
        int Number(string screenName, int fallback) => (int)getNumber.Invoke(null, new object[] { screenName, fallback })!;
        Check(Number(@"\\.\DISPLAY3", 1) == 3 && Number(@"\\.\display12", 1) == 12,
            "Identification numbers must match Windows screen names independent of letter case.");
        Check(Number("unrecognized", 2) == 2 && Number(@"\\.\DISPLAY0", 2) == 2 && Number(@"\\.\DISPLAY2147483648", 2) == 2,
            "Malformed or unavailable Windows screen numbers must use a safe fallback.");
        return checks;
    }

    private sealed class FakeDisplay : Display
    {
        public FakeDisplay(string path) : base(new FakeDisplayDevice(path)) { }
        public override bool IsAvailable => false;
    }

    private sealed class FakeDisplayDevice : DisplayDevice
    {
        public FakeDisplayDevice(string path) : base(path, "Regression display", "Regression key") { }
    }
}
