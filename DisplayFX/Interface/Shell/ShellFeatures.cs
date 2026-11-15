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
    private string _profileSearch = "";
    private ObservableCollection<OperationEntry>? _statusHistory;
    private DateTimeOffset? _pauseUntil;
    private string? _candidateApplication;
    private DateTimeOffset _candidateSince;
    public ObservableCollection<OperationEntry> StatusHistory => _statusHistory ??= new();
    public IEnumerable<DisplayFX.Interface.Monitors.MonitorViewModel> VisibleMonitors => Monitors.OrderByDescending(monitor => monitor.Monitor.IsFavorite);
    public OperationSeverity StatusSeverity { get; private set; }
    public string StatusIcon => StatusSeverity switch { OperationSeverity.Success => "\uE73E", OperationSeverity.Error => "\uEA39", OperationSeverity.Warning => "\uE7BA", _ => "\uE946" };
    public Brush StatusBrush => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : StatusSeverity switch { OperationSeverity.Success => Brushes.SeaGreen, OperationSeverity.Error => Brushes.IndianRed, OperationSeverity.Warning => Brushes.DarkGoldenrod, _ => Application.Current?.TryFindResource("AccentBlueBrush") as Brush ?? Brushes.SteelBlue };
    public bool IsApplying => _profileWork > 0 || _previewStarting;
    public string ApplyingText => IsApplying ? LocalizationController.Instance["Applying colors…"] : "";
    public string ApplyActionLabel => Computer.Features.ShowActionLabels ? LocalizationController.Instance["Apply"] : "▶";
    public string RevertActionLabel => Computer.Features.ShowActionLabels ? LocalizationController.Instance["Revert"] : "↶";
    public string SaveActionLabel => Computer.Features.ShowActionLabels ? LocalizationController.Instance["Save"] : "▣";
    public string ProfileSearch
    {
        get => _profileSearch;
        set { _profileSearch = value ?? ""; NotifyOfPropertyChange(); NotifyOfPropertyChange(nameof(VisibleProfiles)); }
    }
    public IEnumerable<ProfileViewModel> VisibleProfiles => (SelectedMonitor?.Profiles ?? Enumerable.Empty<ProfileViewModel>())
        .Where(profile => string.IsNullOrWhiteSpace(ProfileSearch) || profile.Name.Contains(ProfileSearch, StringComparison.OrdinalIgnoreCase) ||
            (profile.Profile.LinkedExecutablePath?.Contains(ProfileSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
            profile.Profile.AdditionalExecutablePaths.Any(path => path.Contains(ProfileSearch, StringComparison.OrdinalIgnoreCase)) ||
            profile.MonitorViewModel.Name.Contains(ProfileSearch, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(profile => profile.Profile.IsFavorite);
    public bool IsAutomationPaused => _pauseUntil.HasValue && _pauseUntil > DateTimeOffset.UtcNow;
    public string AutomationPauseLabel => LocalizationController.Instance[IsAutomationPaused ? "Resume automation" : "Pause automation"];
    public void ToggleAutomationPause()
    {
        _pauseUntil = IsAutomationPaused ? null : DateTimeOffset.MaxValue;
        _appSwitching.Refresh();
        NotifyOfPropertyChange(nameof(IsAutomationPaused)); NotifyOfPropertyChange(nameof(AutomationPauseLabel));
    }
    public void PauseAutomationFor(int minutes)
    {
        _pauseUntil = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(minutes, 1, 1440));
        NotifyOfPropertyChange(nameof(IsAutomationPaused)); NotifyOfPropertyChange(nameof(AutomationPauseLabel));
    }
    public void PauseAutomationUntil(DateTimeOffset time)
    {
        if (time <= DateTimeOffset.Now) throw new ArgumentOutOfRangeException(nameof(time), "Choose a future resume time.");
        _pauseUntil = time.ToUniversalTime(); NotifyOfPropertyChange(nameof(IsAutomationPaused)); NotifyOfPropertyChange(nameof(AutomationPauseLabel));
    }
    private void RecordStatus(string message)
    {
        var text = message.ToLowerInvariant();
        StatusSeverity = text.Contains("failed") || text.Contains("could not") || text.Contains("unavailable") ? OperationSeverity.Error :
            text.Contains("check ") || text.Contains("changed during") ? OperationSeverity.Warning :
            text.Contains("saved") || text.Contains("merged") || text.Contains("exported") || text.Contains("imported") ? OperationSeverity.Success : OperationSeverity.Information;
        if (!string.IsNullOrWhiteSpace(message))
        {
            if (StatusHistory.LastOrDefault()?.Message != message) StatusHistory.Add(new(DateTimeOffset.Now, StatusSeverity, message));
            while (StatusHistory.Count > 200) StatusHistory.RemoveAt(0);
        }
        NotifyOfPropertyChange(nameof(StatusSeverity)); NotifyOfPropertyChange(nameof(StatusIcon)); NotifyOfPropertyChange(nameof(StatusBrush));
    }
    private void ToggleFavorite(Guid guid)
    {
        var profile = FindProfile(guid);
        if (profile == null) return;
        var previous = profile.Profile.IsFavorite;
        profile.Profile.IsFavorite = !previous;
        try { Write(); profile.RefreshMetadata(); NotifyOfPropertyChange(nameof(VisibleProfiles)); }
        catch (Exception ex) { profile.Profile.IsFavorite = previous; ReportFailure("Could not save favorite.", ex); }
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
        if (_settingsOpen) { OperationStatus = "Close Settings or Workspace before applying a tray profile."; return; }
        var profile = Monitors.SelectMany(monitor => monitor.Profiles).FirstOrDefault(item => item.Profile.Id == profileId);
        if (profile == null) { OperationStatus = "The requested profile is not connected."; return; }
        await ApplyProfile(profile);
    }
    public void OpenWelcomeGuide()
    {
        MessageBox.Show(LocalizationController.WelcomeText, LocalizationController.Instance["Welcome to DisplayFX"], MessageBoxButton.OK, MessageBoxImage.Information);
        var previous = Computer.Features.OnboardingCompleted;
        Computer.Features.OnboardingCompleted = true;
        try { Write(); } catch (Exception ex) { Computer.Features.OnboardingCompleted = previous; ReportFailure("Could not save welcome preference.", ex); }
    }
}
