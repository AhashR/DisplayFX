using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using DisplayFX.Global;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.Monitors;
using DisplayFX.Interface.Profiles;
using DisplayFX.Interface.Settings;
using DisplayFX.Interface.Shell;
using DisplayFX.Objects.Entities;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Action = System.Action;

namespace DisplayFX.Interface.Workspace;

public class WorkspaceViewModel : Screen, IAsyncDisposable
{
    private readonly ShellViewModel _shell;
    private readonly DataController _data;
    private readonly MonitorBrightnessController _hardware;
    private readonly DisplayCache _cache;
    private string _status = "";
    private MonitorViewModel? _selectedMonitor;
    private ProfileViewModel? _selectedProfile;
    private ProfileMetadata _metadata = new();
    private Computer? _importSource;
    private bool _busy;
    private bool _reportReady;
    private readonly Dictionary<Guid, ProfileMetadata> _metadataDrafts = new();
    private readonly Dictionary<string, IReadOnlyList<NamedChoice>> _choices = new();
    private List<NamedChoice>? _scheduleTargets;
    public WorkspaceViewModel(ShellViewModel shell, DataController data, MonitorBrightnessController hardware, DisplayCache cache)
    {
        _shell = shell; _data = data; _hardware = hardware; _cache = cache;
        Preferences = JsonConvert.DeserializeObject<FeatureSettings>(JsonConvert.SerializeObject(shell.Computer.Features))!;
        AutomationSettings = new SettingsViewModel(shell.Computer, new RegistryController(), data);
        SelectedMonitor = shell.SelectedMonitor ?? shell.Monitors.FirstOrDefault();
        SelectedProfile = shell.SelectedProfile ?? SelectedMonitor?.Profiles.FirstOrDefault();
        RefreshSnapshots();
        LocalizationController.Instance.PropertyChanged += OnLanguageChanged;
    }
    public FeatureSettings Preferences { get; }
    public IEnumerable<ProfileSet> SavedSets => Preferences.ProfileSets.ToList();
    public IEnumerable<DisplayFX.Objects.Entities.Monitor> DisconnectedMonitors => _shell.Computer.Monitors.Where(saved => !Monitors.Any(live => ReferenceEquals(live.Monitor, saved))).ToList();
    public DisplayFX.Objects.Entities.Monitor? SavedDisplayToMatch { get; set; }
    public BrightnessSchedule? SelectedSchedule { get; set; }
    public MonitorPreferences MonitorSettings { get; private set; } = new();
    public SettingsViewModel AutomationSettings { get; }
    private IEnumerable<NamedChoice> Choices(params string[] ids)
    {
        var key = string.Join("|", ids);
        if (!_choices.TryGetValue(key, out var choices)) _choices[key] = choices = ids.Select(id => new NamedChoice(id, id)).ToList();
        return choices;
    }
    public IEnumerable<NamedChoice> ThemeChoices => Choices("System", "Dark", "Light");
    public IEnumerable<NamedChoice> DensityChoices => Choices("Compact", "Comfortable");
    public IEnumerable<NamedChoice> PopupOrderChoices => Choices("Saved", "Name", "Favorites");
    public IEnumerable<NamedChoice> LanguageChoices => new[] { new NamedChoice("en", "English"), new NamedChoice("nl", "Nederlands") };
    public IEnumerable<NamedChoice> PowerChoices => Choices("Any", "AC", "Battery");
    public IEnumerable<NamedChoice> TargetChoices => Choices("All", "Selected", "Pointer");
    public IEnumerable<NamedChoice> UpdateChoices => Choices("Stable", "Test");
    public IEnumerable<NamedChoice> PresetChoices => Choices("Work", "Film", "Evening", "Gaming");
    public IEnumerable<NamedChoice> ScheduleTargets => _scheduleTargets ??= new[] { new NamedChoice(null, "All displays") }.Concat(Monitors.Select(monitor => new NamedChoice(monitor.Monitor.DisplayDevicePath, monitor.Name, false))).ToList();
    public IEnumerable<NamedChoice> SolarChoices => Choices("Fixed", "Sunrise", "Sunset");
    public string PresetName { get; set; } = "Work";
    public int PauseMinutes { get; set; } = 30;
    public string PauseUntilText { get; set; } = "20:00";
    public string ReferenceProfileName => SelectedMonitor?.Profiles.FirstOrDefault(profile => profile.Profile.Id == SelectedMonitor.Monitor.ReferenceProfileId)?.Name ?? "Default / previous manual profile";
    public string SetName { get; set; } = "My displays";
    public ProfileSet? SelectedSet { get; set; }
    public ObservableCollection<MonitorViewModel> Monitors => _shell.Monitors;
    public IEnumerable<ProfileViewModel> Profiles => SelectedMonitor?.Profiles ?? Enumerable.Empty<ProfileViewModel>();
    public MonitorViewModel? CopyTarget { get; set; }
    public MonitorViewModel? SelectedMonitor
    {
        get => _selectedMonitor;
        set
        {
            _selectedMonitor = value; NotifyOfPropertyChange(); NotifyOfPropertyChange(nameof(Profiles));
            SelectedProfile = value?.Profiles.FirstOrDefault();
            MonitorSettings = value == null ? new() : MonitorPreferences.From(value.Monitor); NotifyOfPropertyChange(nameof(MonitorSettings));
            CompatibilityNotes = Preferences.Compatibility.FirstOrDefault(record => record.DevicePath == value?.Monitor.DisplayDevicePath)?.Notes ?? ""; NotifyOfPropertyChange(nameof(CompatibilityNotes));
            VcpFeatures.Clear(); HardwareSummary = "Run a read-only scan for this display."; NotifyOfPropertyChange(nameof(HardwareSummary));
            SelectedVcp = null;
            NotifyOfPropertyChange(nameof(ReferenceProfileName));
        }
    }
    public ProfileViewModel? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (_selectedProfile != null) _metadataDrafts[_selectedProfile.Profile.Id] = Metadata;
            _selectedProfile = value;
            Metadata = value == null ? new() : _metadataDrafts.TryGetValue(value.Profile.Id, out var draft) ? draft : ProfileMetadata.From(value.Profile);
            NotifyOfPropertyChange();
        }
    }
    public ProfileMetadata Metadata { get => _metadata; private set { _metadata = value; NotifyOfPropertyChange(); } }
    public string Status { get => _status; private set { _status = value; NotifyOfPropertyChange(); } }
    public bool IsBusy { get => _busy; private set { _busy = value; NotifyOfPropertyChange(); NotifyOfPropertyChange(nameof(CanExportReport)); } }
    public bool CanExportReport => _reportReady && !IsBusy;
    public ObservableCollection<OperationEntry> History => _shell.StatusHistory;
    public ObservableCollection<VcpFeature> VcpFeatures { get; } = new();
    private VcpFeature? _selectedVcp;
    public VcpFeature? SelectedVcp
    {
        get => _selectedVcp;
        set { _selectedVcp = value; VcpValueInput = (value?.Current ?? value?.AllowedValues.FirstOrDefault() ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture); NotifyOfPropertyChange(); NotifyOfPropertyChange(nameof(VcpValueInput)); }
    }
    public string VcpValueInput { get; set; } = "0";
    public string HardwareSummary { get; private set; } = "";
    public string CompatibilityNotes { get; set; } = "";
    public string ReportPreview { get; private set; } = "Run a read-only self-test to prepare a report.";
    private bool _includeDisplayNames, _includeDevicePaths;
    public bool IncludeDisplayNames { get => _includeDisplayNames; set { _includeDisplayNames = value; _reportReady = false; NotifyOfPropertyChange(); NotifyOfPropertyChange(nameof(CanExportReport)); } }
    public bool IncludeDevicePaths { get => _includeDevicePaths; set { _includeDevicePaths = value; _reportReady = false; NotifyOfPropertyChange(); NotifyOfPropertyChange(nameof(CanExportReport)); } }
    public ObservableCollection<NamedChoice> Snapshots { get; } = new();
    public string? SelectedSnapshot { get; set; }
    public string StorageLocation => _data.DataPath;
    public ObservableCollection<ImportChoice> ImportChoices { get; } = new();
    private ImportChoice? _selectedImportChoice;
    public ImportChoice? SelectedImportChoice { get => _selectedImportChoice; set { _selectedImportChoice = value; NotifyOfPropertyChange(); } }
    public bool IncludeImportedPreferences { get; set; }
    public string ImportSummary { get; private set; } = "Choose a backup to preview its profiles before importing.";

    public void SavePreferences() => Guard(() =>
    {
        if (Preferences.Validate() is string error) throw new InvalidDataException(error);
        var previous = _shell.Computer.Features;
        _shell.Computer.Features = JsonConvert.DeserializeObject<FeatureSettings>(JsonConvert.SerializeObject(Preferences))!;
        try { _shell.PersistFeatureChanges(); } catch { _shell.Computer.Features = previous; throw; }
        Status = "Preferences saved.";
    });
    public void SaveMetadata() => Guard(() =>
    {
        var profile = SelectedProfile?.Profile ?? throw new InvalidOperationException("Select a profile.");
        var previous = ProfileMetadata.From(profile); var timestamp = profile.ModifiedAt;
        Metadata.Apply(profile); profile.ModifiedAt = DateTimeOffset.UtcNow;
        try
        {
            if (ProfileTransferController.GetExecutableConflict(profile) is string conflict) throw new InvalidDataException(conflict);
            _shell.PersistFeatureChanges();
        }
        catch { previous.Apply(profile); profile.ModifiedAt = timestamp; throw; }
        Status = "Profile details saved.";
    });
    public void SetReference() => Guard(() =>
    {
        var monitor = SelectedMonitor?.Monitor ?? throw new InvalidOperationException("Select a monitor.");
        var previous = monitor.ReferenceProfileId;
        monitor.ReferenceProfileId = SelectedProfile?.Profile.Id;
        try { _shell.PersistFeatureChanges(); } catch { monitor.ReferenceProfileId = previous; throw; }
        Status = "Reference profile saved.";
        NotifyOfPropertyChange(nameof(ReferenceProfileName));
    });
    public void CopyProfile() => Guard(() =>
    {
        if (SelectedProfile == null || CopyTarget == null) throw new InvalidOperationException("Select a source profile and target monitor.");
        if (MessageBox.Show("The same values can look different on another panel. Copy saved colors without shortcuts or app rules?", "Copy colors", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
        _shell.AddColorCopy(SelectedProfile.Profile, CopyTarget); Status = "Saved colors copied. Apply the new profile to use them.";
    });
    public void CreatePreset() => Guard(() =>
    {
        var monitor = SelectedMonitor ?? throw new InvalidOperationException("Select a monitor.");
        var colors = PresetName switch
        {
            "Film" => new ProfileSetting(.5, .52, 1, .5), "Evening" => new ProfileSetting(.48, .5, 1, .5) { TemperatureKelvin = 5000 },
            "Gaming" => new ProfileSetting(.5, .5, 1, .6), _ => new ProfileSetting(.5, .5, 1, .5)
        };
        _shell.AddColorCopy(new Profile(monitor.Monitor, LocalizationController.Instance[PresetName], colors, false), monitor);
        Status = "Editable preset created. These are starting points, not calibrated settings.";
    });
    public void CaptureSet() => Guard(() =>
    {
        var name = SetName.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new InvalidDataException("Use a set name of 1–80 characters.");
        var set = new ProfileSet { Name = name, Entries = Monitors.SelectMany(monitor => monitor.Profiles.Where(profile => profile.IsActive)
            .Select(profile => new ProfileSetEntry { DevicePath = monitor.Monitor.DisplayDevicePath, ProfileId = profile.Profile.Id })).ToList() };
        if (set.Entries.Count == 0) throw new InvalidOperationException("No saved profile is active.");
        Preferences.ProfileSets.Add(set); SavePreferences(); NotifyOfPropertyChange(nameof(SavedSets));
    });
    public async Task ApplySet() => await GuardAsync(async () => { await _shell.ApplySet(SelectedSet ?? throw new InvalidOperationException("Select a saved set.")); Status = "Profile set applied."; });
    public void PauseAutomation() { _shell.PauseAutomationFor(PauseMinutes); Status = $"Automatic profile switching paused for {Math.Clamp(PauseMinutes, 1, 1440)} minute(s)."; }
    public void ToggleAutomation() { _shell.ToggleAutomationPause(); Status = _shell.AutomationPauseLabel; }
    public void PauseUntil() => Guard(() =>
    {
        if (!TimeSpan.TryParseExact(PauseUntilText, @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out var time) || time >= TimeSpan.FromDays(1))
            throw new InvalidDataException("Enter a resume time in HH:mm format.");
        var local = DateTime.Today + time;
        if (local <= DateTime.Now) local = local.AddDays(1);
        _shell.PauseAutomationUntil(new DateTimeOffset(local)); Status = "Automatic profiles paused until " + local.ToString("g");
    });
    public void SaveAutomation() => Guard(() => { AutomationSettings.SaveBrightnessAutomation(); Status = AutomationSettings.SettingsStatus; });
    public void Welcome() => _shell.OpenWelcomeGuide();
    public void ReportInputError() => Status = "Correct invalid numeric input before saving.";
    public void AddSchedule() => AutomationSettings.AddSchedule();
    public void RemoveSchedule() { if (SelectedSchedule != null) AutomationSettings.RemoveSchedule(SelectedSchedule); }
    public void UseSelectedShortcutTarget()
    {
        Preferences.TargetDevicePath = SelectedMonitor?.Monitor.DisplayDevicePath;
        Preferences.BrightnessTarget = "Selected"; NotifyOfPropertyChange(nameof(Preferences));
        Status = "Selected shortcut target edited. Apply preferences to save.";
    }
    public void SaveMonitorSettings() => Guard(() =>
    {
        var monitor = SelectedMonitor?.Monitor ?? throw new InvalidOperationException("Select a monitor.");
        var previous = MonitorPreferences.From(monitor);
        MonitorSettings.Apply(monitor);
        try { _shell.PersistFeatureChanges(); } catch { previous.Apply(monitor); throw; }
        Status = "Monitor brightness group, limits and preference saved.";
    });

    public async Task ScanHardware() => await GuardAsync(ScanHardwareCore);
    private async Task ScanHardwareCore()
    {
        var monitor = SelectedMonitor ?? throw new InvalidOperationException("Select a monitor.");
        var result = await Task.Run(() =>
        {
            var advanced = DisplayCapabilitiesController.IsAdvancedColorEnabled(monitor.Display);
            var level = _hardware.GetBrightness(monitor.Display);
            var features = _hardware.ReadVcpFeatures(monitor.Display);
            return (advanced, level, features);
        });
        if (!ReferenceEquals(monitor, SelectedMonitor)) return;
        VcpFeatures.Clear(); foreach (var feature in result.features) VcpFeatures.Add(feature);
        HardwareSummary = $"{monitor.Name}\n{monitor.ResolutionText}\nAdvanced color (HDR/WCG): {(result.advanced.HasValue ? result.advanced.Value ? "active — standard gamma blocked" : "inactive" : "unknown")}\nBacklight: {(result.level.HasValue ? result.level.Value + "%" : "unavailable")}\nAdvertised adjustable VCP features: {result.features.Count}\nStandard Windows gamma supports compatible drivers. Digital Vibrance requires an NVIDIA connection. Additional AMD/Intel saturation APIs are not available in this build.";
        NotifyOfPropertyChange(nameof(HardwareSummary));
        Status = "Read-only scan completed. Advertised capabilities do not guarantee every firmware command will succeed.";
    }
    public async Task ApplyVcp() => await GuardAsync(async () =>
    {
        var monitor = SelectedMonitor ?? throw new InvalidOperationException("Select a monitor.");
        var feature = SelectedVcp ?? throw new InvalidOperationException("Select an advertised VCP feature.");
        if (!uint.TryParse(VcpValueInput, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException("Enter a whole, nonnegative monitor command value. No command was sent.");
        if (feature.Code == 0x10 && feature.Maximum is > 0)
        {
            var normalized = Math.Round(value * 100d / feature.Maximum.Value);
            if (normalized < monitor.Monitor.MinimumBrightness || normalized > monitor.Monitor.MaximumBrightness)
                throw new InvalidOperationException("This backlight value is outside the saved minimum/maximum for this monitor.");
        }
        if (!await Task.Run(() => _hardware.SetVcpFeature(monitor.Display, feature.Code, value)))
            throw new InvalidOperationException("The monitor did not accept this value, or the feature is unavailable. Input and power changes may disconnect the panel.");
        if (feature.Code == 0x10 && _hardware.GetLastKnownBrightness(monitor.Display) is int brightness) _shell.RememberHardwareBrightness(monitor.Display, brightness);
        Status = "Monitor command accepted. Input or power changes can make the monitor temporarily unreachable.";
        await ScanHardwareCore();
    });
    public async Task Reconnect() => await GuardAsync(async () =>
    {
        var monitor = SelectedMonitor ?? throw new InvalidOperationException("Select a monitor.");
        await Task.Run(_cache.Refresh); _hardware.InvalidateDisplayTransport(monitor.Display); _shell.ReconnectDisplay(monitor); await ScanHardwareCore();
    });
    public async Task MatchSavedDisplay() => await GuardAsync(async () =>
    {
        var saved = SavedDisplayToMatch ?? throw new InvalidOperationException("Choose a disconnected saved display.");
        var target = SelectedMonitor ?? throw new InvalidOperationException("Choose the connected physical display.");
        if (MessageBox.Show($"Match saved display '{saved.DisplayName}' to connected display '{target.Name}'? Saved profiles are retained, current target profiles are copied, and a backup snapshot is made first. Unsaved target drafts are not transferred.", "Match displays", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _shell.ReassociateDisplay(saved, target);
        SelectedMonitor = Monitors.FirstOrDefault(); NotifyOfPropertyChange(nameof(DisconnectedMonitors)); Status = "Saved display configuration matched to the connected panel.";
    });
    public void SaveCompatibility() => Guard(() =>
    {
        var monitor = SelectedMonitor ?? throw new InvalidOperationException("Select a monitor.");
        if (CompatibilityNotes.Length > 4000) throw new InvalidDataException("Use up to 4000 characters.");
        var record = new CompatibilityRecord { DevicePath = monitor.Monitor.DisplayDevicePath, Notes = CompatibilityNotes };
        Preferences.Compatibility.RemoveAll(item => item.DevicePath == record.DevicePath); Preferences.Compatibility.Add(record); SavePreferences();
        Status = "Compatibility notes saved.";
    });
    public async Task SelfTest() => await GuardAsync(async () =>
    {
        var displays = Monitors.ToList();
        var includeNames = IncludeDisplayNames; var includePaths = IncludeDevicePaths;
        var observations = await Task.Run(() => displays.Select((monitor, index) =>
        {
            try { return new { Display = includeNames ? monitor.Name : "Display " + (index + 1), DevicePath = includePaths ? monitor.Monitor.DisplayDevicePath : null,
                Brightness = _hardware.GetBrightness(monitor.Display), AdvancedColor = DisplayCapabilitiesController.IsAdvancedColorEnabled(monitor.Display), Error = (string?)null }; }
            catch (Exception ex) { return new { Display = "Display " + (index + 1), DevicePath = (string?)null, Brightness = (int?)null, AdvancedColor = (bool?)null, Error = (string?)ex.GetType().Name }; }
        }).ToList());
        ReportPreview = JsonConvert.SerializeObject(new { App = "DisplayFX", Version = AppVersion.Current, Time = DateTimeOffset.UtcNow, ReadOnly = true, Displays = observations }, Formatting.Indented);
        _reportReady = includeNames == IncludeDisplayNames && includePaths == IncludeDevicePaths;
        NotifyOfPropertyChange(nameof(ReportPreview)); Status = "Read-only self-test completed. The report preview is exactly what Export report writes.";
    });
    public void ExportReport() => Guard(() =>
    {
        if (!_reportReady) throw new InvalidOperationException("Run a read-only self-test before exporting a report.");
        var dialog = new SaveFileDialog { Filter = "Diagnostic report (*.json)|*.json", FileName = "DisplayFX-report.json" };
        if (dialog.ShowDialog() == true) { File.WriteAllText(dialog.FileName, ReportPreview); Status = "Report saved locally."; }
    });
    public void OpenLogs() => Guard(() => Process.Start(new ProcessStartInfo(LoggingController.LogDirectory) { UseShellExecute = true }));
    public void CreateSnapshot() => Guard(() => { _data.CreateSnapshot(_shell.Computer); RefreshSnapshots(); Status = "Snapshot created."; });
    public void OpenBackupFolder() => Guard(() => { Directory.CreateDirectory(_data.SnapshotDirectory); Process.Start(new ProcessStartInfo(_data.SnapshotDirectory) { UseShellExecute = true }); });
    private void RefreshSnapshots() { Snapshots.Clear(); foreach (var path in _data.GetSnapshots()) Snapshots.Add(new(path, Path.GetFileNameWithoutExtension(path), false)); }
    public void PreviewBackup() => Guard(() => LoadImport(_data.ReadSnapshot(SelectedSnapshot ?? throw new InvalidOperationException("Select a history snapshot."))));
    public void ChooseImport() => Guard(() =>
    {
        var dialog = new OpenFileDialog { Filter = "DisplayFX backup (*.json)|*.json" };
        if (dialog.ShowDialog() == true) LoadImport(ProfileTransferController.ReadSettings(dialog.FileName));
    });
    private void LoadImport(Computer source)
    {
        _importSource = source; ImportChoices.Clear();
        foreach (var monitor in source.Monitors)
        {
            var currentMonitor = _shell.Computer.Monitors.FirstOrDefault(current => string.Equals(current.DisplayDevicePath, monitor.DisplayDevicePath, StringComparison.OrdinalIgnoreCase));
            foreach (var profile in monitor.Profiles) ImportChoices.Add(new()
            {
                Selected = true, Monitor = monitor, Profile = profile,
                CurrentProfile = currentMonitor?.Profiles.FirstOrDefault(current => current.Id == profile.Id) ??
                    currentMonitor?.Profiles.FirstOrDefault(current => string.Equals(current.Name, profile.Name, StringComparison.OrdinalIgnoreCase))
            });
        }
        SelectedImportChoice = ImportChoices.FirstOrDefault(); NotifyOfPropertyChange(nameof(SelectedImportChoice));
        ImportSummary = $"{source.Monitors.Count} display(s), {ImportChoices.Count} profile(s). Select profiles, then preview the merge. Existing colors and unsaved edits are retained until confirmation.";
        NotifyOfPropertyChange(nameof(ImportSummary));
    }
    private Computer BuildSelectedImport()
    {
        if (_importSource == null) throw new InvalidOperationException("Choose a backup first.");
        var chosen = ImportChoices.Where(choice => choice.Selected).ToList();
        if (chosen.Count == 0) throw new InvalidOperationException("Select at least one profile.");
        var import = JsonConvert.DeserializeObject<Computer>(JsonConvert.SerializeObject(IncludeImportedPreferences ? _importSource : _shell.Computer))!;
        import.Monitors = chosen.GroupBy(choice => choice.Monitor).Select(group =>
        {
            var monitor = JsonConvert.DeserializeObject<DisplayFX.Objects.Entities.Monitor>(JsonConvert.SerializeObject(group.Key))!;
            var names = group.Select(choice => choice.Profile.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            monitor.Profiles.RemoveAll(profile => !names.Contains(profile.Name));
              if (!monitor.Profiles.Any(profile => profile.Id == monitor.ReferenceProfileId)) monitor.ReferenceProfileId = null;
            foreach (var profile in monitor.Profiles) profile.Monitor = monitor;
              // Retained local sets must continue to refer to their local profiles,
              // even when a backup contains a modified profile with the same ID.
              if (!IncludeImportedPreferences)
              {
                  foreach (var profile in monitor.Profiles) profile.Id = Guid.NewGuid();
                  monitor.ReferenceProfileId = null;
              }
            return monitor;
        }).ToList();
          if (IncludeImportedPreferences)
          {
              foreach (var set in import.Features.ProfileSets)
                  set.Entries.RemoveAll(entry => !import.Monitors.Any(monitor =>
                      string.Equals(monitor.DisplayDevicePath, entry.DevicePath, StringComparison.OrdinalIgnoreCase) &&
                      monitor.Profiles.Any(profile => profile.Id == entry.ProfileId)));
              import.Features.ProfileSets.RemoveAll(set => set.Entries.Count == 0);
          }
        return import;
    }
    public void PreviewMerge() => Guard(() =>
    {
        var merged = ProfileTransferController.MergeSettings(_shell.Computer, BuildSelectedImport());
        var capacities = merged.Monitors.Select(monitor => $"{monitor.DisplayName}: {_shell.Computer.Monitors.FirstOrDefault(existing => existing.DisplayDevicePath == monitor.DisplayDevicePath)?.Profiles.Count ?? 0} → {monitor.Profiles.Count}/{ProfileTransferController.MaximumProfilesPerMonitor}");
        ImportSummary = $"Merge is valid: {merged.Monitors.Count} saved display(s), {merged.Monitors.Sum(monitor => monitor.Profiles.Count)} profiles after import.\n" + string.Join("\n", capacities) + "\nExisting active profiles are retained; imported duplicate names are renamed. Apply selected import to confirm.";
        NotifyOfPropertyChange(nameof(ImportSummary));
    });
    public async Task ApplySelectedImport() => await GuardAsync(async () =>
    {
        var import = BuildSelectedImport();
        ProfileTransferController.MergeSettings(_shell.Computer, import);
        var temporary = Path.Combine(Path.GetTempPath(), "DisplayFX-import-" + Guid.NewGuid().ToString("N") + ".json");
        try { ProfileTransferController.ExportSettings(import, temporary); await _shell.ImportSettings(temporary); Status = _shell.OperationStatus; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        SelectedMonitor = Monitors.FirstOrDefault(); RefreshSnapshots();
    });
    public void EnablePortable() => Guard(() =>
    {
        var destination = new DataController(Path.Combine(AppContext.BaseDirectory, "Data"), Path.Combine(AppContext.BaseDirectory, "missing.json"));
        if (File.Exists(destination.DataPath)) throw new InvalidOperationException("Portable settings already exist. Back them up before changing storage mode.");
        destination.Write(_shell.Computer);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "portable.flag"), "DisplayFX portable mode\n");
        Status = "Portable settings prepared. Restart DisplayFX from this folder. Current storage remains active until restart.";
    });
    public void PreparePerUser() => Guard(() =>
    {
        var marker = Path.Combine(AppContext.BaseDirectory, "portable.flag");
        if (!File.Exists(marker)) { Status = "Per-user storage is already selected for the next launch."; return; }
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayFX");
        var destination = new DataController(directory, Path.Combine(AppContext.BaseDirectory, "missing.json"));
        if (File.Exists(destination.DataPath))
        {
            if (MessageBox.Show("Per-user settings already exist. Back them up and replace them with this portable configuration? The storage switch takes effect after restart.", "Switch storage", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var existing = destination.Load();
            if (existing.IsFailed) throw new InvalidDataException("Existing per-user settings could not be backed up. Resolve the file error before switching.");
            destination.CreateSnapshot(existing.Value);
        }
        destination.Write(_shell.Computer); File.Delete(marker);
        Status = "Per-user settings prepared. Restart DisplayFX. The portable settings file has been retained.";
    });
    public async Task CheckUpdates() => await GuardAsync(async () =>
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DisplayFX/" + AppVersion.Current);
        var releases = JArray.Parse(await client.GetStringAsync("https://api.github.com/repos/AhashR/DisplayFX/releases?per_page=20"));
        var release = releases.OfType<JObject>().FirstOrDefault(item => item.Value<bool?>("draft") != true && (Preferences.UpdateChannel == "Test" || item.Value<bool?>("prerelease") != true));
        if (release == null) { Status = "No published release was found."; return; }
        var tag = release.Value<string>("tag_name") ?? "unknown";
        Status = $"Installed: {AppVersion.Current}; latest {Preferences.UpdateChannel} release: {tag}. Open release page to review changes and choose an installer. No installation was started.";
    });
    public void OpenReleases() => WebsiteLauncher.OpenWebsite("https://github.com/AhashR/DisplayFX/releases");
    private void Guard(Action action) { if (IsBusy) return; try { action(); } catch (Exception ex) { Status = ex.Message; } }
    private async Task GuardAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); } catch (Exception ex) { Status = ex.Message; } finally { IsBusy = false; }
    }
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        foreach (var choice in _choices.Values.SelectMany(items => items).Concat(_scheduleTargets ?? Enumerable.Empty<NamedChoice>())) choice.Refresh();
    }
    public async ValueTask DisposeAsync() { LocalizationController.Instance.PropertyChanged -= OnLanguageChanged; await AutomationSettings.DeactivateAsync(true); }
}

public sealed class ProfileMetadata
{
    public string Notes { get; set; } = "";
    public string Icon { get; set; } = "●";
    public string MarkerColor { get; set; } = "#80B5FA";
    public bool IsFavorite { get; set; }
    public string Applications { get; set; } = "";
    public int RulePriority { get; set; }
    public bool FullscreenOnly { get; set; }
    public string PowerCondition { get; set; } = "Any";
    public static ProfileMetadata From(Profile profile) => new() { Notes = profile.Notes, Icon = profile.Icon, MarkerColor = profile.MarkerColor,
        IsFavorite = profile.IsFavorite, Applications = string.Join(Environment.NewLine, profile.AdditionalExecutablePaths), RulePriority = profile.RulePriority,
        FullscreenOnly = profile.FullscreenOnly, PowerCondition = profile.PowerCondition };
    public void Apply(Profile profile)
    {
        profile.Notes = Notes; profile.Icon = Icon; profile.MarkerColor = MarkerColor; profile.IsFavorite = IsFavorite;
        profile.AdditionalExecutablePaths = Applications.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(path => path.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        profile.RulePriority = RulePriority; profile.FullscreenOnly = FullscreenOnly; profile.PowerCondition = PowerCondition;
    }
}
public sealed class ImportChoice
{
    public bool Selected { get; set; }
    public DisplayFX.Objects.Entities.Monitor Monitor { get; set; } = null!;
    public Profile Profile { get; set; } = null!;
    public Profile? CurrentProfile { get; set; }
    public string Comparison
    {
        get
        {
            if (CurrentProfile == null) return LocalizationController.Instance["New profile; no current profile on this display to compare."];
            var changes = new List<string>();
            void Compare(string label, object? before, object? after)
            {
                var left = Convert.ToString(before, System.Globalization.CultureInfo.InvariantCulture) ?? "—";
                var right = Convert.ToString(after, System.Globalization.CultureInfo.InvariantCulture) ?? "—";
                if (left != right) changes.Add($"{LocalizationController.Instance[label]}: {left} → {right}");
            }
            var current = CurrentProfile.ProfileSetting; var backup = Profile.ProfileSetting;
            Compare("Color brightness", current.Brightness, backup.Brightness); Compare("Contrast", current.Contrast, backup.Contrast);
            Compare("Vibrance", current.DigitalVibrance, backup.DigitalVibrance); Compare("Gamma", current.Gamma, backup.Gamma);
            Compare("Red gain", current.RedGain, backup.RedGain); Compare("Green gain", current.GreenGain, backup.GreenGain); Compare("Blue gain", current.BlueGain, backup.BlueGain);
            Compare("Temperature tint", current.TemperatureKelvin, backup.TemperatureKelvin);
            Compare("Name", CurrentProfile.Name, Profile.Name); Compare("Profile notes", CurrentProfile.Notes, Profile.Notes);
            Compare("Icon", CurrentProfile.Icon, Profile.Icon); Compare("Marker color", CurrentProfile.MarkerColor, Profile.MarkerColor);
            Compare("Favorite", CurrentProfile.IsFavorite, Profile.IsFavorite);
            Compare("Application", CurrentProfile.LinkedExecutablePath, Profile.LinkedExecutablePath);
            Compare("Additional application paths", string.Join("; ", CurrentProfile.AdditionalExecutablePaths), string.Join("; ", Profile.AdditionalExecutablePaths));
            Compare("Rule priority", CurrentProfile.RulePriority, Profile.RulePriority); Compare("Full screen only", CurrentProfile.FullscreenOnly, Profile.FullscreenOnly);
            Compare("Power condition", CurrentProfile.PowerCondition, Profile.PowerCondition);
            Compare("Shortcut", $"{CurrentProfile.HotkeyModifiers} {CurrentProfile.HotkeyKey}", $"{Profile.HotkeyModifiers} {Profile.HotkeyKey}");
            return changes.Count == 0 ? LocalizationController.Instance["No saved color or rule differences."] : string.Join(Environment.NewLine, changes);
        }
    }
    public string MonitorName => Monitor.DisplayName;
    public string ProfileName => Profile.Name;
}
public sealed class MonitorPreferences
{
    public string Group { get; set; } = "";
    public int Offset { get; set; }
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;
    public bool Favorite { get; set; }
    public static MonitorPreferences From(DisplayFX.Objects.Entities.Monitor monitor) => new() { Group = monitor.BrightnessGroup, Offset = monitor.BrightnessOffset, Minimum = monitor.MinimumBrightness, Maximum = monitor.MaximumBrightness, Favorite = monitor.IsFavorite };
    public void Apply(DisplayFX.Objects.Entities.Monitor monitor) { monitor.BrightnessGroup = Group.Trim(); monitor.BrightnessOffset = Offset; monitor.MinimumBrightness = Minimum; monitor.MaximumBrightness = Maximum; monitor.IsFavorite = Favorite; }
}
public sealed class NamedChoice : INotifyPropertyChanged
{
    private readonly string _text;
    private readonly bool _translate;
    public NamedChoice(string? id, string text, bool translate = true) { Id = id; _text = text; _translate = translate; }
    public string? Id { get; }
    public string Label => _translate ? LocalizationController.Instance[_text] : _text;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Label)));
}
