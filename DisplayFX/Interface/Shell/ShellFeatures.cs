using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.Profiles;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Interface.Shell;

public partial class ShellViewModel
{
    public IEnumerable<DisplayFX.Interface.Monitors.MonitorViewModel> VisibleMonitors => Monitors;
    public OperationSeverity StatusSeverity { get; private set; }
    public string StatusIcon => StatusSeverity switch { OperationSeverity.Success => "\uE73E", OperationSeverity.Error => "\uEA39", OperationSeverity.Warning => "\uE7BA", _ => "\uE946" };
    public Brush StatusBrush => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : StatusSeverity switch { OperationSeverity.Success => Brushes.SeaGreen, OperationSeverity.Error => Brushes.IndianRed, OperationSeverity.Warning => Brushes.DarkGoldenrod, _ => Application.Current?.TryFindResource("AccentBlueBrush") as Brush ?? Brushes.SteelBlue };
    public bool IsApplying => _profileWork > 0;
    public string ApplyActionLabel => _manualApplyInProgress ? "Applying…" : "Apply";
    public string RevertActionLabel => "Revert";
    public string SaveActionLabel => "Save";
    public IEnumerable<ProfileViewModel> VisibleProfiles => (IEnumerable<ProfileViewModel>?)SelectedMonitor?.Profiles ?? Enumerable.Empty<ProfileViewModel>();
    private void RecordStatus(string message)
    {
        var text = message.ToLowerInvariant();
        StatusSeverity = text.Contains("failed") || text.Contains("could not") || text.Contains("unavailable") ? OperationSeverity.Error :
            text.Contains("check ") || text.Contains("changed during") ? OperationSeverity.Warning :
            text.Contains("applied") || text.Contains("saved") || text.Contains("merged") || text.Contains("exported") || text.Contains("imported") ? OperationSeverity.Success : OperationSeverity.Information;
        NotifyOfPropertyChange(nameof(StatusSeverity)); NotifyOfPropertyChange(nameof(StatusIcon)); NotifyOfPropertyChange(nameof(StatusBrush));
    }
    private void MoveProfile(Guid guid, int direction)
    {
        var profile = FindProfile(guid);
        if (profile == null) return;
        var monitor = profile.MonitorViewModel;
        var original = monitor.Profiles.IndexOf(profile);
        var next = Math.Clamp(original + direction, 0, monitor.Profiles.Count - 1);
        if (original == next) return;
        monitor.Monitor.Profiles.Remove(profile.Profile); monitor.Monitor.Profiles.Insert(next, profile.Profile);
        try { Write(); monitor.Profiles.Move(original, next); NotifyOfPropertyChange(nameof(VisibleProfiles)); }
        catch (Exception ex)
        {
            monitor.Monitor.Profiles.Remove(profile.Profile); monitor.Monitor.Profiles.Insert(original, profile.Profile);
            ReportFailure("Could not reorder profiles.", ex);
        }
    }
    public async Task ApplyProfileById(Guid profileId)
    {
        if (_settingsOpen) { OperationStatus = "Close Settings before applying a tray profile."; return; }
        var profile = Monitors.SelectMany(monitor => monitor.Profiles).FirstOrDefault(item => item.Profile.Id == profileId);
        if (profile == null) { OperationStatus = "The requested profile is not connected."; return; }
        await ApplyProfile(profile);
    }
}
