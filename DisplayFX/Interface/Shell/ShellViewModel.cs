using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Caliburn.Micro;
using NLog;
using NvAPIWrapper.Display;
using DisplayFX.Global;
using DisplayFX.Global.Controllers;
using DisplayFX.Global.Extensions;
using DisplayFX.Interface.Monitors;
using DisplayFX.Interface.Profiles;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.Factories;
using DisplayFX.Objects.Factories.Interfaces;
using DisplayFX.Objects.HandleEvents;
using System.Windows.Input;
using NHotkey.Wpf;
using Monitor = DisplayFX.Objects.Entities.Monitor;

namespace DisplayFX.Interface.Shell;

public partial class ShellViewModel : Conductor<IScreen>, IHandle<ProfileSettingsEvent>
{
    private readonly DataController _dataController;
    private readonly BrightnessPersistenceController _brightnessPersistenceController;
    private readonly DisplayController _displayController;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger _logger;
    private readonly MonitorViewModelFactory _monitorViewModelFactory;
    private readonly MonitorTopologyController _monitorTopologyController;
    private readonly MonitorIdentificationService _monitorIdentificationService;
    private readonly ComputerFactory _computerFactory;
    private readonly Dictionary<string, MonitorViewModel> _retainedMonitors = new(StringComparer.OrdinalIgnoreCase);
    private bool _settingsOpen;
    private string _operationStatus = string.Empty;

    private readonly DisplayWindowManager _nvidiaDisplayWindowManager;
    private readonly ProfileFactory _profileFactory;
    private readonly IProfileViewModelFactory _profileViewModelFactory;

    private readonly RegistryController _registryController;
    private Computer _computer = null!;
    private ObservableCollection<MonitorViewModel> _monitors = null!;
    private List<Display>? _nvidiaDisplays;
    private bool _nvidiaInitialized;
    private bool _profileSettingsIsDirty;
    private MonitorViewModel? _selectedMonitor;
    private Display? _selectedNvidiaMonitor;
    private ProfileViewModel? _selectedProfile;
    private HotkeyManager? _hotkeyManager;
    private readonly ProcessController _processController;
    private readonly AppProfileSwitchingController _appSwitching;
    private readonly ColorUpdateDiagnostics _colorDiagnostics;
    private DispatcherTimer? _appTimer;
    private readonly SemaphoreSlim _colorGate = new(1, 1);
    private readonly Dictionary<Monitor, long> _profileRequests = new();
    private long _requestSequence;
    private int _modelVersion;
    private int _profileWork;
    private bool _switchingApplication;
    private DateTime _nextAppRetry;

    public ShellViewModel(
        IEventAggregator eventAggregator,
        MonitorViewModelFactory monitorViewModelFactory,
        DataController dataController,
        BrightnessPersistenceController brightnessPersistenceController,
        IProfileViewModelFactory profileViewModelFactory,
        ProfileFactory profileFactory,
        ILogger logger,
        DisplayController displayController,
        DisplayWindowManager nvidiaDisplayWindowManager,
        RegistryController registryController,
        MonitorTopologyController monitorTopologyController,
        MonitorIdentificationService monitorIdentificationService,
        ComputerFactory computerFactory,
        ProcessController processController,
        AppProfileSwitchingController appSwitching,
        ColorUpdateDiagnostics colorDiagnostics)
    {
        _eventAggregator = eventAggregator;
        _monitorViewModelFactory = monitorViewModelFactory;
        _dataController = dataController;
        _brightnessPersistenceController = brightnessPersistenceController;
        _profileViewModelFactory = profileViewModelFactory;
        _profileFactory = profileFactory;
        _logger = logger;
        _displayController = displayController;
        _nvidiaDisplayWindowManager = nvidiaDisplayWindowManager;
        _registryController = registryController;
        _monitorTopologyController = monitorTopologyController;
        _monitorIdentificationService = monitorIdentificationService;
        _computerFactory = computerFactory;
        _processController = processController;
        _appSwitching = appSwitching;
        _colorDiagnostics = colorDiagnostics;


        _eventAggregator.SubscribeOnUIThread(this);

        Start();
    }

    public ObservableCollection<MonitorViewModel> Monitors
    {
        get => _monitors;
        set
        {
            if (Equals(value, _monitors)) return;
            _monitors = value;
            NotifyOfPropertyChange();
        }
    }

    public override string DisplayName
    {
        get => "DisplayFX";
        set { }
    }

    public MonitorViewModel? SelectedMonitor
    {
        get => _selectedMonitor;
        set
        {
            if (Equals(value, _selectedMonitor)) return;
            _selectedMonitor = value;
            NotifyOfPropertyChange();
            NotifyOfPropertyChange(nameof(SelectedProfile));
            NotifyOfPropertyChange(nameof(CanAddProfile));
            NotifyOfPropertyChange(nameof(ProfileGroupBoxText));
            NotifyOfPropertyChange(nameof(VisibleProfiles));
        }
    }

    public ProfileViewModel? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (Equals(value, _selectedProfile)) return;
            _selectedProfile = value;
            ProfileSettingsIsDirty = value?.ProfileSettings?.IsDirty ?? false;
            NotifyOfPropertyChange();
            NotifyOfPropertyChange(nameof(CanApply));
            NotifyOfPropertyChange(nameof(CanSave));
        }
    }

    public bool ProfileSettingsIsDirty
    {
        get => _profileSettingsIsDirty;
        set
        {
            if (value == _profileSettingsIsDirty) return;
            _profileSettingsIsDirty = value;
            NotifyOfPropertyChange();
            NotifyOfPropertyChange(nameof(CanApply));
            NotifyOfPropertyChange(nameof(CanSave));
        }
    }

    public bool CanApply => !IsApplying && SelectedProfile?.ProfileSettings is { HasInputErrors: false };
    public bool CanSave => CanApply && ProfileSettingsIsDirty;
    private bool _manualApplyInProgress;
    public bool CanAddProfile => SelectedMonitor is not null && SelectedMonitor.Profiles.Count < ProfileTransferController.MaximumProfilesPerMonitor;
    public bool CanIdentifyMonitors => Monitors.Count > 0;
    public string OperationStatus
    {
        get => _operationStatus;
        private set { _operationStatus = value; RecordStatus(value); NotifyOfPropertyChange(); NotifyOfPropertyChange(nameof(HasOperationStatus)); }
    }
    public bool HasOperationStatus => !string.IsNullOrWhiteSpace(OperationStatus);
    public bool IsStartWithWindows
    {
        get => Computer.IsStartWithWindows;
        set
        {
            if (value == Computer.IsStartWithWindows) return;
            Computer.IsStartWithWindows = value;
            NotifyOfPropertyChange();
            Write();
            OnIsStartWithWindowsChanged();
        }
    }

    public async Task OpenSettings()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        foreach (var profile in Monitors.SelectMany(monitor => monitor.Profiles))
            UnregisterProfileHotkey(profile);
        try
        {
            await _nvidiaDisplayWindowManager.OpenSettings(Computer, ImportSingleProfile, ExportSelectedProfile, CheckForUpdates,
                () => CanAddProfile, () => SelectedProfile != null,
                () => SelectedMonitor == null ? "No display selected." :
                    SelectedProfile == null ? SelectedMonitor.Name : $"{SelectedMonitor.Name} · {SelectedProfile.Name}");
        }
        finally
        {
            _settingsOpen = false;
            RefreshAllHotkeys();
            _appSwitching.Refresh();
        }
    }

    public Display? SelectedNvidiaMonitor
    {
        get => _selectedNvidiaMonitor;
        set
        {
            if (Equals(value, _selectedNvidiaMonitor)) return;
            _selectedNvidiaMonitor = value;
            NotifyOfPropertyChange();
        }
    }

    public Computer Computer
    {
        get => _computer;
        set
        {
            if (Equals(value, _computer)) return;
            _computer = value;
            NotifyOfPropertyChange();
        }
    }

    public string ProfileGroupBoxText =>
        $"Profiles [{(SelectedMonitor == null ? 0 : SelectedMonitor.Profiles.Count)}/{ProfileTransferController.MaximumProfilesPerMonitor}]";


    public Task HandleAsync(ProfileSettingsEvent message, CancellationToken cancellationToken)
    {
        if (ReferenceEquals(message.Profile, SelectedProfile?.Profile))
            ProfileSettingsIsDirty = message.IsDirty;
        NotifyOfPropertyChange(nameof(CanApply));
        NotifyOfPropertyChange(nameof(CanSave));
        return Task.CompletedTask;
    }

    private void OnIsStartWithWindowsChanged()
    {
        _registryController.RegisterForStartWithWindows(IsStartWithWindows);
    }

    private void Start()
    {
        _monitors = new ObservableCollection<MonitorViewModel>();

        _dataController
            .Load()
            .IfSuccess(computer =>
            {
                Computer = computer;
                AppearanceController.Apply();
                _brightnessPersistenceController.Attach(computer);
                Computer.Monitors.ForEach(BuildMonitorViewModel);
                LoadNvidiaDisplays();
                SelectPrimaryMonitorByDefault();
            })
            ;
    }

    private void SelectPrimaryMonitorByDefault()
    {
        if (Monitors == null || Monitors.Count == 0) return;

        var primaryMonitor = Monitors.FirstOrDefault(m =>
            m.Display != null && m.Display.DisplayScreen != null && m.Display.DisplayScreen.IsPrimary)
            ?? Monitors.FirstOrDefault(m =>
            {
                var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
                return primaryScreen != null && !string.IsNullOrEmpty(primaryScreen.DeviceName) &&
                       m.ScreenName.Equals(primaryScreen.DeviceName, StringComparison.OrdinalIgnoreCase);
            })
            ?? Monitors.FirstOrDefault();

        if (primaryMonitor != null)
        {
            SelectMonitor(primaryMonitor);
            SetSelectedProfile();
        }
    }

    private void BuildMonitorViewModel(Monitor monitor)
    {
        _monitorViewModelFactory
            .Create(monitor)
            .IfSuccess(monitorViewModel =>
            {
                monitorViewModel.Profiles.ForEach(WireProfileEvents);
                monitorViewModel.IsSelectedChanged += OnMonitorViewModelIsSelectedChanged;
                monitorViewModel.CustomNameChanged = _ =>
                {
                    Write();
                    UpdateTrayTooltip();
                };
                _retainedMonitors[monitor.DisplayDevicePath] = monitorViewModel;

                Monitors.Add(monitorViewModel);
                NotifyOfPropertyChange(nameof(VisibleMonitors));
            });
    }

    private void LoadNvidiaDisplays()
    {
        try
        {
            _nvidiaDisplays = EnumerateNvidiaDisplays();
        }
        catch (Exception e)
        {
            // Driver replacement invalidates old handles. Retry discovery on the next
            // topology change or manual refresh, even when startup had no NVIDIA API.
            _nvidiaDisplays = new List<Display>();
            _driverRecoveryAttempts++;
            _nextDriverRecovery = DateTimeOffset.UtcNow.AddSeconds(Math.Min(30, 5 * _driverRecoveryAttempts));
            _logger.Warn(e, "NVIDIA displays are unavailable; refresh displays after the driver is ready.");
        }
    }

    protected virtual List<Display> EnumerateNvidiaDisplays()
    {
        if (!_nvidiaInitialized)
        {
            NvAPIWrapper.NVIDIA.Initialize();
            _nvidiaInitialized = true;
        }
        return Display.GetDisplays().ToList();
    }

    private void UpdateTrayTooltip()
    {
        GlobalEvents.UpdateToolTip?.Invoke();
    }


    public void IdentifyMonitors() => _monitorIdentificationService.Show(Monitors);

    private void OnMonitorTopologyChanged(object? sender, EventArgs args) => RefreshMonitors();

    public virtual void RefreshMonitors()
    {
        _driverRecoveryAttempts = 0;
        var selected = SelectedMonitor;
        var selectedProfile = SelectedProfile;
        var refresh = _computerFactory.RefreshConnectedMonitors(Computer);
        if (refresh.IsFailed)
        {
            OperationStatus = "Could not refresh monitors. Check display connections and retry.";
            return;
        }
        _modelVersion++;
        var paths = refresh.Value.Monitors.Select(monitor => monitor.DisplayDevicePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var retained in _retainedMonitors.ToList())
            if (!string.Equals(retained.Key, retained.Value.Monitor.DisplayDevicePath, StringComparison.OrdinalIgnoreCase))
            { _retainedMonitors.Remove(retained.Key); _retainedMonitors[retained.Value.Monitor.DisplayDevicePath] = retained.Value; }
        foreach (var monitor in Monitors.ToList())
        {
            if (paths.Contains(monitor.Monitor.DisplayDevicePath) && _monitorViewModelFactory.Refresh(monitor))
                continue;
            foreach (var profile in monitor.Profiles) UnregisterProfileHotkey(profile);
            Monitors.Remove(monitor);
            // Keep the view model and its drafts for a reconnect.
        }
        foreach (var model in Computer.Monitors.Where(monitor => paths.Contains(monitor.DisplayDevicePath)))
        {
            if (Monitors.Any(monitor => ReferenceEquals(monitor.Monitor, model))) continue;
            if (_retainedMonitors.TryGetValue(model.DisplayDevicePath, out var retained))
            {
                if (_monitorViewModelFactory.Refresh(retained)) Monitors.Add(retained);
            }
            else BuildMonitorViewModel(model);
        }
        LoadNvidiaDisplays();
        if (selected != null && Monitors.Contains(selected))
        {
            SelectMonitor(selected);
            SelectProfile(selectedProfile);
        }
        else
        {
            if (Monitors.Count == 0) SelectMonitor(null);
            else SelectPrimaryMonitorByDefault();
            SetSelectedProfile();
        }
        RefreshAllHotkeys();
        NotifyOfPropertyChange(nameof(CanIdentifyMonitors));
        NotifyOfPropertyChange(nameof(VisibleMonitors));
        UpdateTrayTooltip();
        try { Write(); OperationStatus = $"{Monitors.Count} connected display(s). Saved profiles retained."; }
        catch (Exception ex) { ReportFailure("Monitors refreshed, but settings could not be saved.", ex); }
    }

    private ProfileViewModel? FindProfile(Guid guid) => Monitors.SelectMany(monitor => monitor.Profiles)
        .FirstOrDefault(profile => profile.Guid == guid);

    private async Task DuplicateProfile(Guid guid)
    {
        var source = FindProfile(guid);
        if (source == null) return;
        if (source.MonitorViewModel.Profiles.Count >= ProfileTransferController.MaximumProfilesPerMonitor)
        { OperationStatus = "Each monitor supports 100 profiles. Remove an unused profile before duplicating."; return; }
        var monitor = source.MonitorViewModel;
        var result = await _nvidiaDisplayWindowManager.OpenProfileNameViewModel("Duplicate profile",
            ProfileTransferController.GetUniqueName(monitor.Monitor, source.Name),
            monitor.Profiles.Select(profile => profile.Name));
        if (result.IsFailed || monitor.Profiles.Count >= ProfileTransferController.MaximumProfilesPerMonitor) return;
        try
        {
            AddTransferredProfile(monitor, ProfileTransferController.CloneProfile(source.Profile, monitor.Monitor, result.Value));
            OperationStatus = "Profile duplicated.";
        }
        catch (Exception ex) { ReportFailure("Could not duplicate profile.", ex); }
    }

    private async Task RenameProfile(Guid guid)
    {
        var profile = FindProfile(guid);
        if (profile == null || profile.IsDefault) return;
        var result = await _nvidiaDisplayWindowManager.OpenProfileNameViewModel("Rename profile", profile.Name,
            profile.MonitorViewModel.Profiles.Where(other => other != profile).Select(other => other.Name));
        if (result.IsFailed) return;
        var previous = profile.Name;
        try
        {
            ProfileTransferController.RenameProfile(profile.Profile, result.Value);
            try { Write(); } catch { profile.Profile.Name = previous; throw; }
            profile.RefreshName();
            UpdateTrayTooltip();
        }
        catch (Exception ex) { ReportFailure("Could not rename profile.", ex); }
    }

    private void AddTransferredProfile(MonitorViewModel monitor, Profile profile)
    {
        if (monitor.Profiles.Count >= ProfileTransferController.MaximumProfilesPerMonitor) throw new InvalidOperationException("Each monitor supports up to 100 profiles.");
        monitor.Monitor.Profiles.Add(profile);
        try { Write(); } catch { monitor.Monitor.Profiles.Remove(profile); throw; }
        var model = _profileViewModelFactory.Create(profile, monitor);
        WireProfileEvents(model);
        monitor.Profiles.Add(model);
        NotifyOfPropertyChange(nameof(VisibleProfiles));
        if (Monitors.Contains(monitor)) SelectProfile(model);
        NotifyOfPropertyChange(nameof(CanAddProfile));
        NotifyOfPropertyChange(nameof(ProfileGroupBoxText));
        RefreshAllHotkeys();
    }

    private void ReportFailure(string message, Exception exception)
    {
        _logger.Warn(exception, message);
        OperationStatus = message + " " + exception.Message;
    }

    private void WireProfileEvents(ProfileViewModel profileViewModel)
    {
        profileViewModel.IsSelectedChanged += OnProfileViewModelSelectedChanged;
        profileViewModel.ProfileRemoved += async guid => await OnProfileRemoved(guid);
        profileViewModel.ProfileDuplicateRequested += async guid => await DuplicateProfile(guid);
        profileViewModel.ProfileRenameRequested += async guid => await RenameProfile(guid);
        profileViewModel.IconRequested += SetProfileIcon;
        profileViewModel.ClearShortcutRequested += RemoveExistingProfileShortcut;
        profileViewModel.ProfileMoveRequested += MoveProfile;
        if (profileViewModel.ProfileSettings != null)
            profileViewModel.ProfileSettings.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(profileViewModel.ProfileSettings.IsRecordingHotkey))
                    RefreshAllHotkeys();
            };
        
        // Register hotkey when profile is created/loaded
        RegisterProfileHotkey(profileViewModel);
    }

    private void OnProfileViewModelSelectedChanged(Guid guid, bool value)
    {
        var profile = Monitors.SelectMany(monitor => monitor.Profiles).FirstOrDefault(profile => profile.Guid == guid);
        if (profile == null) return;

        if (value)
        {
            SelectProfile(profile);
        }
        else if (ReferenceEquals(SelectedProfile, profile))
        {
            SelectedProfile = null;
        }

        ProfileSettingsIsDirty = SelectedProfile?.ProfileSettings?.IsDirty ?? false;
        NotifyOfPropertyChange(nameof(CanApply));
        NotifyOfPropertyChange(nameof(CanSave));
    }

    private async Task OnProfileRemoved(Guid guid)
    {
        // Let an in-flight preview settle before deciding whether deletion needs
        // to restore the default profile's hardware colors.
        if (_profileWork > 0)
        {
            await _colorGate.WaitAsync();
            _colorGate.Release();
        }
        var profileViewModel = Monitors.SelectMany(monitor => monitor.Profiles).FirstOrDefault(profile => profile.Guid == guid);
        if (profileViewModel == null || profileViewModel.IsDefault) return;
        var monitor = profileViewModel.MonitorViewModel;

        if (profileViewModel.IsActive)
        {
            var fallback = monitor.Profiles.FirstOrDefault(profile => profile.Profile.Id == monitor.Monitor.ReferenceProfileId && profile.Guid != guid) ?? monitor.Profiles.FirstOrDefault(profile => profile.IsDefault);
            if (fallback == null || !await ApplyProfile(fallback, false)) return;
        }

        var savedIndex = monitor.Monitor.Profiles.IndexOf(profileViewModel.Profile);
        monitor.Monitor.Profiles.Remove(profileViewModel.Profile);
        try { Write(); }
        catch (Exception ex)
        {
            monitor.Monitor.Profiles.Insert(savedIndex, profileViewModel.Profile);
            ReportFailure("Could not remove profile. Your profile and edits have been retained.", ex);
            return;
        }
        UnregisterProfileHotkey(profileViewModel);
        monitor.Profiles.Remove(profileViewModel);
        NotifyOfPropertyChange(nameof(VisibleProfiles));
        _eventAggregator.Unsubscribe(profileViewModel.ProfileSettings!);
        if (ReferenceEquals(SelectedProfile, profileViewModel))
            SetSelectedProfile();

        NotifyOfPropertyChange(nameof(CanAddProfile));
        NotifyOfPropertyChange(nameof(ProfileGroupBoxText));

        RefreshAllHotkeys();
        _appSwitching?.Refresh();
        UpdateTrayTooltip();
        OperationStatus = "Profile removed.";
    }

    private void Write()
    {
        _dataController.Write(Computer);
    }

    private void OnMonitorViewModelIsSelectedChanged(bool isSelected, Guid selectedMonitor)
    {
        if (_changingSelection) return;
        var monitor = Monitors.FirstOrDefault(m => m.Guid == selectedMonitor);
        if (monitor == null) return;
        if (!isSelected)
        {
            if (ReferenceEquals(SelectedMonitor, monitor)) SelectMonitor(monitor);
            return;
        }
        if (ReferenceEquals(SelectedMonitor, monitor)) return;
        SelectMonitor(monitor);
        SetSelectedProfile();
    }

    private bool _changingSelection;

    private void SelectMonitor(MonitorViewModel? monitor)
    {
        _changingSelection = true;
        try
        {
            foreach (var candidate in Monitors)
                candidate.IsSelected = ReferenceEquals(candidate, monitor);
            SelectedMonitor = monitor;
            try { SelectedNvidiaMonitor = monitor == null ? null : _nvidiaDisplays?.FirstOrDefault(d => d.Name == monitor.ScreenName); }
            catch (Exception ex)
            {
                SelectedNvidiaMonitor = null;
                ReportFailure("NVIDIA display connection changed. Refresh displays to reconnect colour controls.", ex);
            }
        }
        finally
        {
            _changingSelection = false;
        }
    }

    private void SelectProfile(ProfileViewModel? profile)
    {
        if (SelectedProfile != profile && SelectedProfile?.ProfileSettings != null)
            SelectedProfile.ProfileSettings.IsRecordingHotkey = false;
        if (profile != null)
            SelectMonitor(profile.MonitorViewModel);

        foreach (var candidate in Monitors.SelectMany(monitor => monitor.Profiles))
        {
            if (!ReferenceEquals(candidate, profile))
                candidate.UnSelect();
        }

        SelectedProfile = profile;
        if (profile != null && !profile.IsSelected)
            profile.IsSelected = true;
    }

    private void SetSelectedProfile()
    {
        SelectProfile(SelectedMonitor?.Profiles.FirstOrDefault(p => p.IsActive) ?? SelectedMonitor?.Profiles.FirstOrDefault());
    }

    public async Task AddProfile()
    {
        if (!CanAddProfile) return;
        var monitor = SelectedMonitor!;
        var result = await _nvidiaDisplayWindowManager.OpenProfileNameViewModel("New profile", "",
            monitor.Profiles.Select(profile => profile.Name));
        if (result.IsFailed || monitor.Profiles.Count >= ProfileTransferController.MaximumProfilesPerMonitor) return;
        try { AddTransferredProfile(monitor, _profileFactory.Create(monitor.Monitor, result.Value)); }
        catch (Exception ex) { ReportFailure("Could not create profile.", ex); }
    }

    internal ProfileViewModel ImportProfileFile(MonitorViewModel monitor, string path)
    {
        if (!Monitors.Contains(monitor) || monitor.Profiles.Count >= ProfileTransferController.MaximumProfilesPerMonitor)
            throw new InvalidOperationException("Choose a connected monitor with room for another profile.");
        var profile = ProfileTransferController.ReadProfile(path, monitor.Monitor);
        AddTransferredProfile(monitor, profile);
        return monitor.Profiles.Single(model => model.Profile.Id == profile.Id);
    }

    public async Task Apply()
    {
        var profile = SelectedProfile;
        if (!CanApply || profile?.ProfileSettings == null) return;
        _manualApplyInProgress = true;
        NotifyOfPropertyChange(nameof(ApplyActionLabel));
        try { await ApplyProfile(profile, settingsOverride: profile.ProfileSettings.ProfileSetting); }
        finally
        {
            _manualApplyInProgress = false;
            NotifyOfPropertyChange(nameof(ApplyActionLabel));
        }
    }

    public async Task Revert()
    {
        var profile = SelectedProfile;
        if (profile == null) return;
        await _eventAggregator.PublishOnUIThreadAsync(new RevertEvent(profile.Profile));
        if (profile.IsActive)
            await ApplyProfile(profile, false);
    }

    public void Update()
    {
        if (!CanSave || SelectedProfile?.ProfileSettings is not { } editor) return;
        try { editor.Save(Write); }
        catch (Exception ex) { ReportFailure("Could not save profile. Your edits remain available to retry.", ex); return; }
        OperationStatus = "Profile saved.";
        ProfileSettingsIsDirty = false;
        _appSwitching?.Refresh();
        
        // Re-register hotkey in case it changed
        RefreshAllHotkeys();
    }

    protected override void OnViewLoaded(object view)
    {
        base.OnViewLoaded(view);
        InitializeHotkeyManager(view);
        _monitorTopologyController.TopologyChanged += OnMonitorTopologyChanged;
        _monitorTopologyController.Start();
        _appTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _appTimer.Tick += OnApplicationTick;
        _appTimer.Start();
        _processController.StartForegroundWatch(() => OnApplicationTick(this, EventArgs.Empty));
        StartFeatureRuntime();
    }

    private async void OnApplicationTick(object? sender, EventArgs args)
    {
        RecoverDriverIfNeeded();
        if (_switchingApplication || _profileWork > 0 || DateTime.UtcNow < _nextAppRetry || _settingsOpen ||
            Monitors.Any(monitor => monitor.Profiles.Any(profile => profile.ProfileSettings?.IsRecordingHotkey == true))) return;
        _switchingApplication = true;
        try
        {
            var application = _processController.GetForegroundExecutablePath();
            await _appSwitching.UpdateAsync(application, Monitors.Select(monitor => monitor.Monitor).ToList(),
                async profile =>
                {
                    var model = Monitors.SelectMany(monitor => monitor.Profiles).FirstOrDefault(model => ReferenceEquals(model.Profile, profile));
                    var success = model != null && await ApplyProfile(model, false);
                    if (!success) _nextAppRetry = DateTime.UtcNow.AddSeconds(30);
                    return success;
                });
        }
        catch (Exception ex) { _logger.Warn(ex, "Could not update the linked application profile."); }
        finally { _switchingApplication = false; }
    }

    private void InitializeHotkeyManager(object view)
    {
        if (view is Window)
        {
            _hotkeyManager = HotkeyManager.Current;
            
            // Register hotkeys for all existing profiles
            foreach (var monitor in Monitors)
            {
                foreach (var profile in monitor.Profiles)
                {
                    RegisterProfileHotkey(profile);
                }
            }
        }
    }

    private void RegisterProfileHotkey(ProfileViewModel profileViewModel)
    {
        profileViewModel.ProfileSettings?.SetHotkeyStatus(null);
        var validationError = GetProfileShortcutValidationError(Computer, profileViewModel.Profile) ??
            ProfileTransferController.GetExecutableConflict(profileViewModel.Profile);
        if (validationError != null)
        {
            profileViewModel.ProfileSettings?.SetHotkeyStatus(validationError);
            return;
        }
        if (_settingsOpen) return;
        if (profileViewModel.Profile.HotkeyKey.HasValue && profileViewModel.Profile.HotkeyKey != Key.None &&
            _hotkeyManager != null)
        {
            try
            {
                var keyGesture = new KeyGesture(
                    profileViewModel.Profile.HotkeyKey.Value,
                    profileViewModel.Profile.HotkeyModifiers ?? ModifierKeys.None);
                
                string hotkeyID = profileViewModel.Guid.ToString();
                _hotkeyManager.AddOrReplace(
                    hotkeyID,
                    keyGesture, 
                    (s, e) =>
                    {
                        ActivateProfile(profileViewModel);
                        e.Handled = true;
                    });
            }
            catch (Exception ex)
            {
                profileViewModel.ProfileSettings?.SetHotkeyStatus("Shortcut unavailable or used by another app. Choose another combination and save.");
                _logger.Warn($"Failed to register hotkey for profile: {profileViewModel.Name}. Error: {ex.Message}");
            }
        }
    }

    public static string? GetProfileShortcutValidationError(Computer computer, Profile profile)
    {
        if (!profile.HotkeyKey.HasValue || profile.HotkeyKey == Key.None) return null;
        var modifiers = profile.HotkeyModifiers ?? ModifierKeys.None;
        try { _ = new KeyGesture(profile.HotkeyKey.Value, modifiers); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException)
        { return "Shortcut needs Ctrl, Alt or Win with a key, or a function key."; }
        if (computer.Monitors.SelectMany(monitor => monitor.Profiles).Any(other => other != profile &&
                other.HotkeyKey == profile.HotkeyKey && (other.HotkeyModifiers ?? ModifierKeys.None) == modifiers))
            return "Shortcut is assigned to another profile. Choose another combination and save.";
        return null;
    }

    private void RefreshAllHotkeys()
    {
        var suspended = _settingsOpen || Monitors.SelectMany(monitor => monitor.Profiles)
            .Any(profile => profile.ProfileSettings?.IsRecordingHotkey == true);
        foreach (var profile in Monitors.SelectMany(monitor => monitor.Profiles)) UnregisterProfileHotkey(profile);
        if (!suspended)
            foreach (var profile in Monitors.SelectMany(monitor => monitor.Profiles)) RegisterProfileHotkey(profile);
    }

    private void UnregisterProfileHotkey(ProfileViewModel profileViewModel)
    {
        if (_hotkeyManager != null)
        {
            _hotkeyManager.Remove(profileViewModel.Guid.ToString());
        }
    }

    private async void ActivateProfile(ProfileViewModel profileViewModel) => await ApplyProfile(profileViewModel);

    private async Task<bool> ApplyProfile(ProfileViewModel profileViewModel, bool updateSelection = true,
        ProfileSetting? settingsOverride = null)
    {
        var monitor = Monitors.FirstOrDefault(m => m.Profiles.Contains(profileViewModel));
        if (monitor == null) return false;
        var requested = ++_requestSequence;
        _profileRequests[monitor.Monitor] = requested;
        var modelVersion = _modelVersion;
        var selectedAtStart = SelectedProfile;
        var monitorAtStart = SelectedMonitor;
        var settings = settingsOverride ?? profileViewModel.Profile.ProfileSetting;
        var snapshot = settings.Copy();
        Display? nvidiaDisplay;
        try { nvidiaDisplay = _nvidiaDisplays?.FirstOrDefault(d => d.Name == monitor.ScreenName); }
        catch (Exception exception) { ReportFailure("The monitor became unavailable.", exception); return false; }
        _profileWork++;
        NotifyApplyAvailability();
        await _colorGate.WaitAsync();
        try
        {
            if (modelVersion != _modelVersion || _profileRequests[monitor.Monitor] != requested || !Monitors.Contains(monitor) || !monitor.Profiles.Contains(profileViewModel)) return false;
            var display = monitor.Display;
            var success = await Task.Run(() => _displayController.UpdateColorSettings(display, snapshot, nvidiaDisplay));
            if (modelVersion != _modelVersion || !Monitors.Contains(monitor) || !monitor.Profiles.Contains(profileViewModel)) return false;
            var currentRequest = _profileRequests[monitor.Monitor] == requested;
            if (!success)
            {
                if (currentRequest)
                    OperationStatus = _displayController.LastError ?? "Profile could not be applied. Retry after checking the display connection.";
                return false;
            }

            if (currentRequest) OperationStatus = _manualApplyInProgress && settingsOverride != null ? "Profile applied." : string.Empty;
            profileViewModel.IsActive = true;
            if (updateSelection && currentRequest) _appSwitching?.ManualSelection(monitor.Monitor);
            foreach (var otherProfile in monitor.Profiles.Where(p => p.Guid != profileViewModel.Guid))
                otherProfile.Deactivate();

            // A slow monitor must not take over the editor after the user navigates away.
            if (updateSelection && currentRequest && ReferenceEquals(SelectedProfile, selectedAtStart) &&
                ReferenceEquals(SelectedMonitor, monitorAtStart))
                SelectProfile(profileViewModel);

            try { Write(); }
            catch (Exception ex)
            {
                _logger.Error(ex, "Profile was applied, but its active state could not be saved.");
                if (currentRequest) OperationStatus = "Profile was applied, but its active state could not be saved.";
            }
            UpdateTrayTooltip();
            return true;
        }
        catch (Exception ex) { ReportFailure("Could not apply profile.", ex); return false; }
        finally { _colorGate.Release(); _profileWork--; NotifyApplyAvailability(); }
    }

    private void NotifyApplyAvailability()
    {
        NotifyOfPropertyChange(nameof(IsApplying));
        NotifyOfPropertyChange(nameof(CanApply));
        NotifyOfPropertyChange(nameof(CanSave));
    }

    protected override async Task OnDeactivateAsync(bool close, CancellationToken cancellationToken)
    {
        if (close)
        {
            _modelVersion++;
            _appTimer?.Stop();
            _processController?.Dispose();
            _commandServer?.Dispose();
            if (_appTimer != null) _appTimer.Tick -= OnApplicationTick;
            _monitorTopologyController.TopologyChanged -= OnMonitorTopologyChanged;
            _monitorTopologyController.Dispose();
            _monitorIdentificationService.Dispose();
            _brightnessPersistenceController.Detach();
            foreach (var profile in Monitors.SelectMany(monitor => monitor.Profiles))
                UnregisterProfileHotkey(profile);
            _eventAggregator.Unsubscribe(this);
        }
        await base.OnDeactivateAsync(close, cancellationToken);
    }
}
