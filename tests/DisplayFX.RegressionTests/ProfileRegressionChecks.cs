using System;
using System.IO;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Input;
using Caliburn.Micro;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.Monitors;
using DisplayFX.Interface.ProfileNames;
using DisplayFX.Interface.Profiles;
using DisplayFX.Interface.ProfileSettings;
using DisplayFX.Interface.Shell;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.Factories;
using DisplayFX.Objects.HandleEvents;
using Monitor = DisplayFX.Objects.Entities.Monitor;

internal static class ProfileRegressionChecks
{
    internal static int Run()
    {
        var checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks++;
        }

        Check(ProfileSettingViewModel.FormatHotkey(ModifierKeys.Control | ModifierKeys.Windows, Key.D9) == "Ctrl + Win + 9",
            "Windows modifiers must be visible in recorded hotkeys.");
        Check(ProfileSettingViewModel.FormatHotkey(null, Key.F8) == "F8",
            "Modifier-free function key shortcuts must be displayed.");
        Check(!new ProfileNameViewModel { ProfileName = "   " }.CanSave,
            "Whitespace-only profile names must not be accepted.");

        // DefaultPlatformProvider executes work inline, so these checks never open a UI or touch monitor hardware.
        var previousPlatformProvider = PlatformProvider.Current;
        PlatformProvider.Current = new DefaultPlatformProvider();
        try
        {
            var events = new EventAggregator();
            var firstMonitor = new Monitor("first", "First", new Size(1920, 1080), 60);
            var secondMonitor = new Monitor("second", "Second", new Size(1920, 1080), 60);
            var firstProfile = new Profile(firstMonitor, "First profile", new ProfileSetting(0.5, 0.5, 1, 0.5), true);
            var secondProfile = new Profile(secondMonitor, "Second profile", new ProfileSetting(0.5, 0.5, 1, 0.5), true);
            firstProfile.HotkeyKey = Key.F8;
            firstProfile.LinkedExecutablePath = @"C:\Games\original.exe";

            var settingsFactory = new ProfileSettingViewModelFactory(events);
            var firstMonitorView = new MonitorViewModel(firstMonitor, null!);
            var secondMonitorView = new MonitorViewModel(secondMonitor, null!);
            var firstProfileView = new ProfileViewModel(firstProfile, firstMonitorView, settingsFactory);
            var secondProfileView = new ProfileViewModel(secondProfile, secondMonitorView, settingsFactory);
            firstMonitorView.Profiles.Add(firstProfileView);
            secondMonitorView.Profiles.Add(secondProfileView);
            firstMonitor.Profiles.Add(firstProfile);
            secondMonitor.Profiles.Add(secondProfile);

            var firstSettings = firstProfileView.ProfileSettings!;
            var secondSettings = secondProfileView.ProfileSettings!;
            firstSettings.Brightness = 0.7;
            firstSettings.Contrast = 0.6;
            firstSettings.Gamma = 1.3;
            firstSettings.DigitalVibrance = 0.8;
            firstSettings.LinkedExecutablePath = @"C:\Games\draft.exe";
            firstSettings.SetRecordedHotkey(ModifierKeys.Windows, Key.D9);
            secondSettings.Brightness = 0.9;
            Check(firstSettings.IsDirty && secondSettings.IsDirty, "Edits must mark their respective profiles dirty.");
            Check(firstProfile.ProfileSetting.Brightness == 0.5 && firstProfile.ProfileSetting.Contrast == 0.5 &&
                  firstProfile.ProfileSetting.Gamma == 1 && firstProfile.ProfileSetting.DigitalVibrance == 0.5 && firstProfile.HotkeyKey == Key.F8 &&
                  firstProfile.HotkeyModifiers == null,
                "Draft edits must not enter the persistent model through unrelated saves.");
            Check(firstProfile.LinkedExecutablePath == @"C:\Games\original.exe", "Executable link edits must remain isolated drafts.");
            try
            {
                firstSettings.Save(() => throw new InvalidOperationException("Storage unavailable"));
                throw new InvalidOperationException("Failed draft persistence should throw.");
            }
            catch (InvalidOperationException ex) when (ex.Message == "Storage unavailable")
            {
                Check(firstProfile.ProfileSetting.Brightness == 0.5 &&
                      firstProfile.HotkeyKey == Key.F8 && firstSettings.IsDirty,
                    "Failed persistence must roll back stored fields and keep the editable draft.");
                Check(firstProfile.LinkedExecutablePath == @"C:\Games\original.exe" && firstSettings.LinkedExecutablePath == @"C:\Games\draft.exe",
                    "Failed saves retain the original executable link and its editable draft.");
            }

            firstProfileView.IsSelected = true;
            firstProfileView.UnSelect();
            firstProfileView.IsSelected = true;
            Check(ReferenceEquals(firstSettings, firstProfileView.ProfileSettings) && firstSettings.IsDirty,
                "Profile selection must preserve settings and the original revert baseline.");

            events.PublishOnUIThreadAsync(new RevertEvent(firstProfile)).GetAwaiter().GetResult();
            Check(firstSettings.Brightness == 0.5 && firstSettings.Contrast == 0.5 && firstSettings.Gamma == 1 &&
                  firstSettings.DigitalVibrance == 0.5, "Revert must restore all four color parameters.");
            Check(firstProfile.HotkeyKey == Key.F8 && firstProfile.HotkeyModifiers == null &&
                  firstSettings.HotkeyDisplayText == "F8",
                "Revert must restore draft shortcut.");
            Check(firstSettings.LinkedExecutablePath == @"C:\Games\original.exe", "Revert must restore the saved executable link.");
            Check(!firstSettings.IsDirty, "A fully reverted profile must be clean.");
            Check(secondSettings.Brightness == 0.9 && secondSettings.IsDirty,
                "Revert must leave other profiles untouched.");

            firstSettings.Brightness = 0.6;
            firstSettings.Save(() => { });
            Check(firstProfile.ProfileSetting.Brightness == 0.6, "Explicit Save must commit the draft into the persistent model.");
            firstSettings.Brightness = 0.7;
            events.PublishOnUIThreadAsync(new RevertEvent(firstProfile)).GetAwaiter().GetResult();
            Check(firstSettings.Brightness == 0.6 && !firstSettings.IsDirty,
                "Saving must advance the revert baseline.");

            // Bypass only the constructor, which enumerates physical GPUs/displays. Exercise the real selection handlers.
            var shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
            typeof(ShellViewModel).GetField("_monitors", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(shell, new ObservableCollection<MonitorViewModel> { firstMonitorView, secondMonitorView });
            var monitorSelected = typeof(ShellViewModel).GetMethod("OnMonitorViewModelIsSelectedChanged",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var profileSelected = typeof(ShellViewModel).GetMethod("OnProfileViewModelSelectedChanged",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            firstMonitorView.IsSelectedChanged += (selected, id) => monitorSelected.Invoke(shell, new object[] { selected, id });
            secondMonitorView.IsSelectedChanged += (selected, id) => monitorSelected.Invoke(shell, new object[] { selected, id });
            firstProfileView.IsSelectedChanged += (id, selected) => profileSelected.Invoke(shell, new object[] { id, selected });
            secondProfileView.IsSelectedChanged += (id, selected) => profileSelected.Invoke(shell, new object[] { id, selected });

            firstMonitorView.IsSelected = true;
            secondMonitorView.IsSelected = true;
            Check(!firstMonitorView.IsSelected && secondMonitorView.IsSelected &&
                  ReferenceEquals(shell.SelectedMonitor, secondMonitorView) && ReferenceEquals(shell.SelectedProfile, secondProfileView),
                "Switching monitors must select exactly one monitor and its active profile.");
            Check(!firstProfileView.IsSelected && secondProfileView.IsSelected && shell.ProfileSettingsIsDirty,
                "Profile selection and dirty state must follow the selected monitor.");
            shell.HandleAsync(new ProfileSettingsEvent(firstProfile, false), CancellationToken.None).GetAwaiter().GetResult();
            Check(shell.ProfileSettingsIsDirty, "A background profile event must not clear the selected profile's dirty state.");
            firstProfileView.IsSelected = true;
            Check(firstMonitorView.IsSelected && !secondMonitorView.IsSelected && firstProfileView.IsSelected &&
                  !secondProfileView.IsSelected && ReferenceEquals(shell.SelectedProfile, firstProfileView),
                "Selecting a profile on another monitor must synchronize both selection indicators.");

            var fixture = Path.Combine(AppContext.BaseDirectory, "profile-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixture);
            try
            {
                var blocked = Path.Combine(fixture, "blocked");
                File.WriteAllText(blocked, "sentinel");
                var data = new DataController(blocked, Path.Combine(fixture, "legacy.json"));
                void SetField(string name, object value) => typeof(ShellViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(shell, value);
                SetField("_computer", new Computer { Monitors = new() { firstMonitor, secondMonitor } });
                SetField("_dataController", data);
                SetField("_logger", NLog.LogManager.GetLogger("ProfileSaveChecks"));
                SetField("_eventAggregator", events);
                var removable = new Profile(firstMonitor, "Removable", new ProfileSetting(0.5, 0.5, 1, 0.5), false);
                var removableView = new ProfileViewModel(removable, firstMonitorView, settingsFactory);
                firstMonitor.Profiles.Add(removable);
                firstMonitorView.Profiles.Add(removableView);
                removableView.ProfileSettings!.Brightness = 0.8;
                var remove = typeof(ShellViewModel).GetMethod("OnProfileRemoved", BindingFlags.Instance | BindingFlags.NonPublic)!;
                remove.Invoke(shell, new object[] { removableView.Guid });
                Check(firstMonitor.Profiles.Contains(removable) && firstMonitorView.Profiles.Contains(removableView) &&
                      removableView.ProfileSettings.IsDirty && shell.OperationStatus.Contains("retained"),
                    "A failed removal must preserve the saved profile, editable view and draft.");
                var savedBrightness = firstProfile.ProfileSetting.Brightness;
                firstSettings.Brightness = 0.9;
                shell.ProfileSettingsIsDirty = true;
                shell.Update();
                Check(firstProfile.ProfileSetting.Brightness == savedBrightness && firstSettings.IsDirty &&
                      shell.ProfileSettingsIsDirty && shell.OperationStatus.Contains("retry"),
                    "Shell Save must report storage errors without discarding the draft or dirty state.");
                File.Delete(blocked);
                remove.Invoke(shell, new object[] { removableView.Guid });
                Check(!firstMonitor.Profiles.Contains(removable) && !firstMonitorView.Profiles.Contains(removableView) &&
                      data.Load().IsSuccess && shell.OperationStatus == "Profile removed.",
                    "Removal must remain retryable after storage recovers.");
            }
            finally { Directory.Delete(fixture, true); }
        }
        finally
        {
            PlatformProvider.Current = previousPlatformProvider;
        }

        return checks;
    }
}
