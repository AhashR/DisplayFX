using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.Workspace;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Interface.Shell;

public partial class ShellViewModel
{
    internal void RememberHardwareBrightness(WindowsDisplayAPI.Display display, int level) => _brightnessPersistenceController.Remember(display, level);
    public async Task OpenWorkspace()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        RefreshAllHotkeys();
        try
        {
            var model = new WorkspaceViewModel(this, _dataController, IoC.Get<MonitorBrightnessController>(), IoC.Get<DisplayCache>());
            var window = new WorkspaceView { DataContext = model, Owner = GetView() as Window };
            ViewModelBinder.Bind(model, window, null);
            window.ShowDialog();
            await model.DisposeAsync();
        }
        finally { _settingsOpen = false; RefreshAllHotkeys(); _appSwitching.Refresh(); }
    }
    internal void PersistFeatureChanges()
    {
        ProfileTransferController.ValidateStoredComputer(Computer);
        Write();
        AppearanceController.Apply(Computer.Features);
        foreach (var monitor in Monitors)
            foreach (var profile in monitor.Profiles) profile.RefreshMetadata();
        foreach (var property in new[] { nameof(VisibleProfiles), nameof(VisibleMonitors), nameof(ApplyActionLabel), nameof(RevertActionLabel), nameof(SaveActionLabel), nameof(AutomationPauseLabel) }) NotifyOfPropertyChange(property);
        _appSwitching.Refresh();
        UpdateTrayTooltip();
    }
    internal void AddColorCopy(Profile source, DisplayFX.Interface.Monitors.MonitorViewModel target, string? name = null)
    {
        var copy = ProfileTransferController.CloneProfile(source, target.Monitor, ProfileTransferController.GetUniqueName(target.Monitor, name ?? source.Name));
        AddTransferredProfile(target, copy);
    }
    internal async Task ApplySet(ProfileSet set)
    {
        var targets = set.Entries.Select(entry =>
            Monitors.FirstOrDefault(monitor => string.Equals(monitor.Monitor.DisplayDevicePath, entry.DevicePath, StringComparison.OrdinalIgnoreCase))?
                .Profiles.FirstOrDefault(profile => profile.Profile.Id == entry.ProfileId)).ToList();
        if (targets.Any(target => target == null)) throw new InvalidOperationException("Every display and profile in the set must be connected before applying it.");
        await CancelPreview();
        var version = _modelVersion;
        var snapshots = new System.Collections.Generic.List<(DisplayFX.Interface.Profiles.ProfileViewModel Profile, DisplayController.ColorSnapshot Colors, NvAPIWrapper.Display.Display? Nvidia)>();
        var previousFlags = Monitors.SelectMany(monitor => monitor.Profiles).ToDictionary(profile => profile, profile => profile.IsActive);
        foreach (var target in targets)
        {
            var monitor = target!.MonitorViewModel;
            var nvidia = _nvidiaDisplays?.FirstOrDefault(display => display.Name == monitor.ScreenName);
            var colors = await Task.Run(() => _displayController.CaptureColors(monitor.Display, nvidia));
            snapshots.Add((target, colors, nvidia));
        }
        var attempted = 0;
        try
        {
            foreach (var target in targets)
            {
                if (version != _modelVersion) throw new InvalidOperationException("Display connections changed while applying the set.");
                attempted++;
                if (!await ApplyProfile(target!, false)) throw new InvalidOperationException(OperationStatus);
            }
            Write();
        }
        catch
        {
            foreach (var snapshot in snapshots.Take(attempted).Reverse())
                if (version == _modelVersion && Monitors.Contains(snapshot.Profile.MonitorViewModel))
                    await Task.Run(() => _displayController.RestoreColors(snapshot.Profile.MonitorViewModel.Display, snapshot.Nvidia, snapshot.Colors));
            foreach (var previous in previousFlags) previous.Key.IsActive = previous.Value;
            try { Write(); } catch (Exception ex) { _logger.Warn(ex, "Could not save the restored profile set state."); }
            throw;
        }
    }
    internal async Task ReassociateDisplay(DisplayFX.Objects.Entities.Monitor saved, DisplayFX.Interface.Monitors.MonitorViewModel target)
    {
        if (!Computer.Monitors.Contains(saved) || Monitors.Any(monitor => ReferenceEquals(monitor.Monitor, saved)))
            throw new InvalidOperationException("Choose a disconnected saved display.");
        await CancelPreview();
        if (_profileWork > 0) throw new InvalidOperationException("Wait until color application finishes before matching displays.");
        var originalPath = saved.DisplayDevicePath;
        var targetIndex = Computer.Monitors.IndexOf(target.Monitor);
        var originalProfiles = saved.Profiles.ToList();
        _dataController.CreateSnapshot(Computer);
        var idMap = new System.Collections.Generic.Dictionary<Guid, Guid>();
        foreach (var profile in target.Monitor.Profiles)
        {
            if (saved.Profiles.Count >= ProfileTransferController.MaximumProfilesPerMonitor) { saved.Profiles = originalProfiles; throw new InvalidOperationException("Matching would exceed the profile limit."); }
            var copy = ProfileTransferController.CloneProfile(profile, saved, ProfileTransferController.GetUniqueName(saved, profile.Name));
            saved.Profiles.Add(copy); idMap[profile.Id] = copy.Id;
        }
        var originalEntries = Computer.Features.ProfileSets.SelectMany(set => set.Entries).Select(entry => (Entry: entry, entry.DevicePath, entry.ProfileId)).ToList();
        saved.DisplayDevicePath = target.Monitor.DisplayDevicePath;
        foreach (var entry in Computer.Features.ProfileSets.SelectMany(set => set.Entries))
        {
            if (entry.DevicePath == originalPath) entry.DevicePath = saved.DisplayDevicePath;
            else if (entry.DevicePath == saved.DisplayDevicePath && idMap.TryGetValue(entry.ProfileId, out var replacement)) entry.ProfileId = replacement;
        }
        Computer.Monitors.Remove(target.Monitor);
        try { Write(); }
        catch
        {
            saved.DisplayDevicePath = originalPath; saved.Profiles = originalProfiles; Computer.Monitors.Insert(targetIndex, target.Monitor);
            foreach (var original in originalEntries) { original.Entry.DevicePath = original.DevicePath; original.Entry.ProfileId = original.ProfileId; }
            throw;
        }
        foreach (var profile in target.Profiles) UnregisterProfileHotkey(profile);
        Monitors.Remove(target); _retainedMonitors.Remove(originalPath); _retainedMonitors.Remove(saved.DisplayDevicePath);
        SelectedMonitor = null; SelectedProfile = null;
        RefreshMonitors();
    }
    internal void ReconnectDisplay(DisplayFX.Interface.Monitors.MonitorViewModel monitor)
    {
        if (_profileWork > 0) throw new InvalidOperationException("Wait for the current color operation before reconnecting a display.");
        _modelVersion++;
        if (!_monitorViewModelFactory.Refresh(monitor)) throw new InvalidOperationException("The selected display is not currently connected.");
        LoadNvidiaDisplays();
        _appSwitching.InvalidateHardware(new[] { monitor.Monitor });
        if (ReferenceEquals(SelectedMonitor, monitor)) SelectMonitor(monitor);
    }
}
