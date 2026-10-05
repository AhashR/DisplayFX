using System.Drawing;
using System.IO;
using DisplayFX.Global.Controllers;
using DisplayFX.Objects.Entities;
using Monitor = DisplayFX.Objects.Entities.Monitor;

internal static class AppProfileRegressionChecks
{
    public static int Run()
    {
        var count = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); count++; }
        Check(ProcessController.MatchesExecutable(@"C:\Games\game.exe", @"c:\games\GAME.EXE"), "Full paths compare without case sensitivity.");
        Check(!ProcessController.MatchesExecutable(@"C:\Games\game.exe", @"D:\Other\game.exe"), "Different executable locations must not accidentally match.");
        Check(ProcessController.MatchesExecutable("game.exe", @"D:\Games\game.exe"), "Legacy filename-only links remain readable.");
        Check(!ProcessController.MatchesExecutable("bad\0.exe", @"C:\Games\game.exe") && !ProcessController.MatchesExecutable(null, "game.exe"), "Malformed links must be safe.");
        var monitor = new Monitor("first", "First", new Size(1920, 1080), 60);
        Profile Make(string name, bool active, string? link = null) => new(monitor, name, new ProfileSetting(.5, .5, 1, .5), active, name == "Default") { LinkedExecutablePath = link };
        var manual = Make("Default", true);
        var game = Make("Game", false, @"C:\Games\game.exe");
        var secondGame = Make("Second game", false, @"C:\Games\second.exe");
        monitor.Profiles.AddRange(new[] { manual, game, secondGame });
        var other = new Monitor("second", "Second", new Size(1920, 1080), 60);
        other.Profiles.Add(new(other, "Default", new(.5, .5, 1, .5), true, true));
        var computer = new Computer { Monitors = new() { monitor, other } };
        var switching = new AppProfileSwitchingController();
        var writes = 0;
        bool Apply(Profile target)
        {
            writes++;
            foreach (var profile in target.Monitor.Profiles) profile.IsActive = ReferenceEquals(profile, target);
            return true;
        }
        void Focus(string? app) => switching.Update(app, computer.Monitors, Apply);
        Focus(@"C:\Games\game.exe");
        Check(game.IsActive && manual.IsActiveBeforeAutomaticSwitch == true && other.Profiles[0].IsActive,
            "A linked executable changes only its monitor and retains its manual baseline.");
        Focus(@"C:\Games\game.exe");
        Check(writes == 1, "An unchanged foreground app must not repeatedly reapply hardware settings.");
        Focus(null);
        Check(game.IsActive, "An inaccessible foreground process must not discard the working profile.");
        Focus(@"C:\Games\second.exe");
        Check(secondGame.IsActive && manual.IsActiveBeforeAutomaticSwitch == true, "Switching between two linked apps retains the original manual profile.");
        Focus(@"C:\Browser\browser.exe");
        Check(manual.IsActive && monitor.Profiles.All(profile => !profile.IsActiveBeforeAutomaticSwitch.HasValue),
            "Leaving linked apps restores the original profile and clears transient baseline metadata.");
        Focus(@"C:\Games\game.exe");
        switching.ManualSelection(monitor);
        Apply(manual);
        Focus(@"C:\Games\game.exe");
        Check(manual.IsActive, "A manual profile selection remains until the foreground app changes.");
        switching.Refresh();
        Focus(@"C:\Games\game.exe");
        Check(game.IsActive, "Saving a newly edited link can refresh matching against the current application.");
        switching.Reset();
        Apply(manual);
        switching.Update(@"C:\Games\game.exe", computer.Monitors, _ => false);
        Check(manual.IsActive && monitor.Profiles.All(profile => !profile.IsActiveBeforeAutomaticSwitch.HasValue),
            "A failed apply cannot leave a fake automatic baseline in saved settings.");
        Focus(@"C:\Games\game.exe");
        Check(game.IsActive, "Failed profile activation can recover on a later update.");
        var duplicate = ProfileTransferController.CloneProfile(game, monitor, "Copy");
        Check(duplicate.LinkedExecutablePath == null, "Duplicating a profile must not create an ambiguous executable trigger.");

        var fixture = Path.Combine(AppContext.BaseDirectory, "app-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var data = new DataController(fixture, Path.Combine(fixture, "missing.json"));
            data.Write(computer);
            var loaded = data.Load().Value;
            Check(loaded.Monitors[0].Profiles[0].IsActive && loaded.Monitors[0].Profiles[1].LinkedExecutablePath == @"C:\Games\game.exe",
                "Restarting during app switching restores the manual profile and keeps its executable link.");
            var backup = Path.Combine(fixture, "backup.json");
            ProfileTransferController.ExportSettings(computer, backup);
            Check(ProfileTransferController.ReadSettings(backup).Monitors[0].Profiles[1].LinkedExecutablePath == game.LinkedExecutablePath,
                "Settings backup and restore must preserve executable links.");
        }
        finally { if (Directory.Exists(fixture)) Directory.Delete(fixture, true); }
        return count;
    }
}
