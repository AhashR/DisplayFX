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
using NvAPIWrapper.GPU;
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

public class ShellViewModel : Conductor<IScreen>, IHandle<ProfileSettingsEvent>
{
    private readonly DataController _dataController;
    private readonly BrightnessPersistenceController _brightnessPersistenceController;
    private readonly DisplayController _displayController;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger _logger;
    private readonly MonitorViewModelFactory _monitorViewModelFactory;
    private readonly BrightnessAutomationController _brightnessAutomationController;
    private readonly MonitorTopologyController _monitorTopologyController;
    private readonly MonitorIdentificationService _monitorIdentificationService;
    private readonly ComputerFactory _computerFactory;
    private readonly Dictionary<string, MonitorViewModel> _retainedMonitors = new(StringComparer.OrdinalIgnoreCase);
    private bool _viewReady;
    private bool _settingsOpen;
    private string _operationStatus = string.Empty;

    private readonly DisplayWindowManager _nvidiaDisplayWindowManager;
    private readonly ProfileFactory _profileFactory;
    private readonly IProfileViewModelFactory _profileViewModelFactory;

    private readonly RegistryController _registryController;
    private Computer _computer = null!;
    private ObservableCollection<MonitorViewModel> _monitors = null!;
    private List<Display>? _nvidiaDisplays;
    private readonly string _displayName;
    private readonly bool _hasNvidiaHardware;
    private bool _profileSettingsIsDirty;
    private MonitorViewModel? _selectedMonitor;
    private Display? _selectedNvidiaMonitor;
    private ProfileViewModel? _selectedProfile;
    private HotkeyManager? _hotkeyManager;
    private readonly ProcessController _processController;
    private readonly AppProfileSwitchingController _appSwitching;
    private DispatcherTimer? _appTimer;

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
        BrightnessAutomationController brightnessAutomationController,
        MonitorTopologyController monitorTopologyController,
        MonitorIdentificationService monitorIdentificationService,
        ComputerFactory computerFactory,
        ProcessController processController,
        AppProfileSwitchingController appSwitching)
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
        _brightnessAutomationController = brightnessAutomationController;
        _monitorTopologyController = monitorTopologyController;
        _monitorIdentificationService = monitorIdentificationService;
        _computerFactory = computerFactory;
        _processController = processController;
        _appSwitching = appSwitching;

        _displayName = $"DisplayFX {AppVersion.Current}";
        try
        {
            var gpus = PhysicalGPU.GetPhysicalGPUs();
            _hasNvidiaHardware = gpus.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "NVIDIA hardware is unavailable; using standard display controls.");
        }

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
        get => _displayName;
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
        }
    }

    public bool CanApply => SelectedProfile?.ProfileSettings != null;
    public bool CanAddProfile => SelectedMonitor is not null && SelectedMonitor.Profiles.Count < 5;
    public bool CanIdentifyMonitors => Monitors.Count > 0;
    public string OperationStatus
    {
        get => _operationStatus;
        private set { _operationStatus = value; NotifyOfPropertyChange(); NotifyOfPropertyChange(nameof(HasOperationStatus)); }
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

    public bool IsApplySettingsOnStart
    {
        get => Computer.IsApplySettingsOnStart;
        set
        {
            if (value == Computer.IsApplySettingsOnStart) return;
            Computer.IsApplySettingsOnStart = value;
            NotifyOfPropertyChange();
            Write();
        }
    }

    public async Task OpenSettings()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        foreach (var profile in Monitors.SelectMany(monitor => monitor.Profiles))
            UnregisterProfileHotkey(profile);
        _brightnessAutomationController.SuspendHotkeys(true);
        try
        {
            var restorePath = await _nvidiaDisplayWindowManager.OpenSettings(Computer);
            if (restorePath != null) ImportSettings(restorePath);
            NotifyOfPropertyChange(nameof(IsApplySettingsOnStart));
        }
        finally
        {
            _settingsOpen = false;
            _brightnessAutomationController.SuspendHotkeys(false);
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
        $"Profiles [{(SelectedMonitor == null ? 0 : SelectedMonitor.Profiles.Count)}/5]";


    public Task HandleAsync(ProfileSettingsEvent message, CancellationToken cancellationToken)
    {
        if (ReferenceEquals(message.Profile, SelectedProfile?.Profile))
            ProfileSettingsIsDirty = message.IsDirty;
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
                _brightnessPersistenceController.Attach(computer);
                _brightnessAutomationController.Attach(computer);
                Computer.Monitors.ForEach(BuildMonitorViewModel);
                LoadNvidiaDisplays();
                SelectPrimaryMonitorByDefault();
                ApplySettingsOnStart();
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
            primaryMonitor.IsSelected = true;
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
            });
    }

    private void LoadNvidiaDisplays()
    {
        if (!_hasNvidiaHardware)
        {
            _nvidiaDisplays = new List<Display>();
            return;
        }

        try
        {
            _nvidiaDisplays = Display.GetDisplays().ToList();
        }
        catch (Exception e)
        {
            _logger.Error(e);
            _nvidiaDisplayWindowManager
                .ShowMessageBox("Failed to load displays connected to GPU. " +
                                "Make sure screen is not being duplicated and or is connected to GPU. " +
                                "Some features may not function properly.");
        }
    }

    private void ApplySettingsOnStart()
    {
        if (!IsApplySettingsOnStart)
            return;

        foreach (var monitorViewModel in Monitors)
        {
            var activeProfile = monitorViewModel.Profiles.FirstOrDefault(p => p.IsActive);
            var nvidiaDisplay = _nvidiaDisplays?.FirstOrDefault(d => d.Name == monitorViewModel.ScreenName);

            if (activeProfile is not null)
                _displayController.UpdateColorSettings(
                    monitorViewModel.Display,
                    activeProfile.Profile.ProfileSetting,
                    nvidiaDisplay);
        }
    }

    private void UpdateTrayTooltip()
    {
        GlobalEvents.UpdateToolTip?.Invoke();
    }


    public void IdentifyMonitors() => _monitorIdentificationService.Show(Monitors);

    private void OnMonitorTopologyChanged(object? sender, EventArgs args) => RefreshMonitors();

    public void RefreshMonitors()
    {
        var selected = SelectedMonitor;
        var selectedProfile = SelectedProfile;
        var refresh = _computerFactory.RefreshConnectedMonitors(Computer);
        if (refresh.IsFailed)
        {
            OperationStatus = "Could not refresh monitors. Check display connections and retry.";
            return;
        }
        var paths = refresh.Value.Monitors.Select(monitor => monitor.DisplayDevicePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            SelectMonitor(null);
            SelectPrimaryMonitorByDefault();
            SetSelectedProfile();
        }
        RefreshAllHotkeys();
        _appSwitching?.Refresh();
        NotifyOfPropertyChange(nameof(CanIdentifyMonitors));
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
        if (source.MonitorViewModel.Profiles.Count >= 5)
        { OperationStatus = "Each monitor supports five profiles. Remove an unused profile before duplicating."; return; }
        var monitor = source.MonitorViewModel;
        var result = await _nvidiaDisplayWindowManager.OpenProfileNameViewModel("Duplicate profile",
            ProfileTransferController.GetUniqueName(monitor.Monitor, source.Name),
            monitor.Profiles.Select(profile => profile.Name));
        if (result.IsFailed || monitor.Profiles.Count >= 5) return;
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
        if (monitor.Profiles.Count >= 5) throw new InvalidOperationException("Each monitor supports up to five profiles.");
        monitor.Monitor.Profiles.Add(profile);
        try { Write(); } catch { monitor.Monitor.Profiles.Remove(profile); throw; }
        var model = _profileViewModelFactory.Create(profile, monitor);
        WireProfileEvents(model);
        monitor.Profiles.Add(model);
        if (Monitors.Contains(monitor)) SelectProfile(model);
        NotifyOfPropertyChange(nameof(CanAddProfile));
        NotifyOfPropertyChange(nameof(ProfileGroupBoxText));
        RefreshAllHotkeys();
    }

    private void ExportProfile(Guid guid)
    {
        var profile = FindProfile(guid);
        if (profile == null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "DisplayFX profile (*.json)|*.json",
            FileName = "DisplayFX-profile.json", DefaultExt = ".json" };
        if (dialog.ShowDialog() != true) return;
        try { ProfileTransferController.ExportProfile(profile.Profile, dialog.FileName); OperationStatus = "Saved profile exported."; }
        catch (Exception ex) { ReportFailure("Could not export profile.", ex); }
    }

    public void ImportSettings(string? path = null)
    {
        if (path == null)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "DisplayFX settings (*.json)|*.json" };
            if (dialog.ShowDialog() != true) return;
            path = dialog.FileName;
        }
        try
        {
            var merged = ProfileTransferController.MergeSettings(Computer, ProfileTransferController.ReadSettings(path));
            // Persist before replacing live models; a bad file or failed write cannot discard drafts.
            var previous = Computer;
            _dataController.Write(merged);
            try
            {
                if (merged.IsStartWithWindows != previous.IsStartWithWindows)
                    _registryController.RegisterForStartWithWindows(merged.IsStartWithWindows);
            }
            catch { _dataController.Write(previous); throw; }
            foreach (var profile in _retainedMonitors.Values.SelectMany(monitor => monitor.Profiles))
            {
                UnregisterProfileHotkey(profile);
                _eventAggregator.Unsubscribe(profile.ProfileSettings!);
            }
            _brightnessAutomationController.Detach();
            _appSwitching.Reset();
            _retainedMonitors.Clear();
            Monitors.Clear();
            Computer = merged;
            _brightnessPersistenceController.Attach(merged);
            _brightnessAutomationController.Attach(merged);
            SelectedMonitor = null;
            SelectedProfile = null;
            RefreshMonitors();
            UpdateTrayTooltip();
            if (_viewReady) _brightnessAutomationController.Start();
            NotifyOfPropertyChange(nameof(IsStartWithWindows));
            NotifyOfPropertyChange(nameof(IsApplySettingsOnStart));
            OperationStatus = "Settings merged. Existing profiles retained; imported controls apply from now on.";
        }
        catch (Exception ex) { ReportFailure("Could not import settings.", ex); }
    }

    private void ReportFailure(string message, Exception exception)
    {
        _logger.Warn(exception, message);
        OperationStatus = message + " " + exception.Message;
    }

    private void WireProfileEvents(ProfileViewModel profileViewModel)
    {
        profileViewModel.IsSelectedChanged += OnProfileViewModelSelectedChanged;
        profileViewModel.ProfileRemoved += OnProfileRemoved;
        profileViewModel.ProfileDuplicateRequested += async guid => await DuplicateProfile(guid);
        profileViewModel.ProfileRenameRequested += async guid => await RenameProfile(guid);
        profileViewModel.ProfileExportRequested += ExportProfile;
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
    }

    private void OnProfileRemoved(Guid guid)
    {
        var profileViewModel = Monitors.SelectMany(monitor => monitor.Profiles).FirstOrDefault(profile => profile.Guid == guid);
        if (profileViewModel == null || profileViewModel.IsDefault) return;
        var monitor = profileViewModel.MonitorViewModel;

        if (profileViewModel.IsActive)
        {
            var fallback = monitor.Profiles.FirstOrDefault(profile => profile.IsDefault);
            if (fallback == null || !ApplyProfile(fallback, false)) return;
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
        if (isSelected && monitor != null)
            SelectMonitor(monitor);
        else if (ReferenceEquals(SelectedMonitor, monitor))
            SelectMonitor(null);

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
            SelectedNvidiaMonitor = _nvidiaDisplays?.FirstOrDefault(d => d.Name == monitor?.ScreenName);
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
        SelectProfile(SelectedMonitor?.Profiles.FirstOrDefault(p => p.IsActive));
    }

    public async Task AddProfile()
    {
        if (!CanAddProfile) return;
        var monitor = SelectedMonitor!;
        var result = await _nvidiaDisplayWindowManager.OpenProfileNameViewModel("New profile", "",
            monitor.Profiles.Select(profile => profile.Name));
        if (result.IsFailed || monitor.Profiles.Count >= 5) return;
        try { AddTransferredProfile(monitor, _profileFactory.Create(monitor.Monitor, result.Value)); }
        catch (Exception ex) { ReportFailure("Could not create profile.", ex); }
    }

    public void Apply()
    {
        if (SelectedProfile?.ProfileSettings == null) return;
        ApplyProfile(SelectedProfile, settingsOverride: SelectedProfile.ProfileSettings.ProfileSetting);
    }

    public async Task Revert()
    {
        var profile = SelectedProfile;
        if (profile == null) return;
        await _eventAggregator.PublishOnUIThreadAsync(new RevertEvent(profile.Profile));
        if (profile.IsActive)
            ApplyProfile(profile, false);
    }

    public void Update()
    {
        if (SelectedProfile == null) return;
        try { SelectedProfile.ProfileSettings!.Save(Write); }
        catch (Exception ex) { ReportFailure("Could not save profile. Your edits remain available to retry.", ex); return; }
        ProfileSettingsIsDirty = false;
        _appSwitching?.Refresh();
        
        // Re-register hotkey in case it changed
        RefreshAllHotkeys();
    }

    protected override void OnViewLoaded(object view)
    {
        base.OnViewLoaded(view);
        InitializeHotkeyManager(view);
        _viewReady = true;
        _monitorTopologyController.TopologyChanged += OnMonitorTopologyChanged;
        _monitorTopologyController.Start();
        _brightnessAutomationController.Start();
        _appTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _appTimer.Tick += OnApplicationTick;
        _appTimer.Start();
    }

    private void OnApplicationTick(object? sender, EventArgs args)
    {
        if (_settingsOpen || Monitors.Any(monitor => monitor.Profiles.Any(profile => profile.ProfileSettings?.IsRecordingHotkey == true))) return;
        try
        {
            _appSwitching.Update(_processController.GetForegroundExecutablePath(), Monitors.Select(monitor => monitor.Monitor),
                profile =>
                {
                    var model = Monitors.SelectMany(monitor => monitor.Profiles).FirstOrDefault(model => ReferenceEquals(model.Profile, profile));
                    return model != null && ApplyProfile(model, false);
                });
        }
        catch (Exception ex) { _logger.Warn(ex, "Could not update the linked application profile."); }
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
        var validationError = GetProfileShortcutValidationError(Computer, profileViewModel.Profile);
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
        if (computer.IsBrightnessShortcutsEnabled &&
            (computer.BrightnessIncreaseKey == profile.HotkeyKey && (computer.BrightnessIncreaseModifiers ?? ModifierKeys.None) == modifiers ||
             computer.BrightnessDecreaseKey == profile.HotkeyKey && (computer.BrightnessDecreaseModifiers ?? ModifierKeys.None) == modifiers))
            return "Shortcut is assigned to brightness controls. Choose another combination and save.";
        return null;
    }

    private void RefreshAllHotkeys()
    {
        var suspended = _settingsOpen || Monitors.SelectMany(monitor => monitor.Profiles)
            .Any(profile => profile.ProfileSettings?.IsRecordingHotkey == true);
        foreach (var profile in Monitors.SelectMany(monitor => monitor.Profiles)) UnregisterProfileHotkey(profile);
        if (!suspended)
            foreach (var profile in Monitors.SelectMany(monitor => monitor.Profiles)) RegisterProfileHotkey(profile);
        _brightnessAutomationController?.SuspendHotkeys(suspended);
    }

    private void UnregisterProfileHotkey(ProfileViewModel profileViewModel)
    {
        if (_hotkeyManager != null)
        {
            _hotkeyManager.Remove(profileViewModel.Guid.ToString());
        }
    }

    private void ActivateProfile(ProfileViewModel profileViewModel) => ApplyProfile(profileViewModel);

    private bool ApplyProfile(ProfileViewModel profileViewModel, bool updateSelection = true,
        ProfileSetting? settingsOverride = null)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                var monitor = Monitors.FirstOrDefault(m => m.Profiles.Contains(profileViewModel));
                if (monitor != null)
                {
                    var nvidiaDisplay = _nvidiaDisplays?.FirstOrDefault(d => d.Name == monitor.ScreenName);
                    
                    if (!_displayController.UpdateColorSettings(
                        monitor.Display,
                        settingsOverride ?? profileViewModel.Profile.ProfileSetting,
                        nvidiaDisplay))
                        return false;

                    profileViewModel.IsActive = true;
                    if (updateSelection) _appSwitching?.ManualSelection(monitor.Monitor);
                    foreach (var otherProfile in monitor.Profiles.Where(p => p.Guid != profileViewModel.Guid))
                    {
                        otherProfile.Deactivate();
                    }

                    if (updateSelection)
                    {
                        SelectProfile(profileViewModel);
                    }

                    try
                    {
                        Write();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Profile was applied, but its active state could not be saved.");
                        _nvidiaDisplayWindowManager.ShowMessageBox("Profile was applied, but its active state could not be saved.");
                    }
                    GlobalEvents.UpdateToolTip?.Invoke();
                    UpdateTrayTooltip();
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error applying profile");
            }
            return false;
        });
    }

    protected override async Task OnDeactivateAsync(bool close, CancellationToken cancellationToken)
    {
        if (close)
        {
            _appTimer?.Stop();
            if (_appTimer != null) _appTimer.Tick -= OnApplicationTick;
            _monitorTopologyController.TopologyChanged -= OnMonitorTopologyChanged;
            _monitorTopologyController.Dispose();
            _monitorIdentificationService.Dispose();
            _brightnessAutomationController.Detach();
            _brightnessPersistenceController.Detach();
            foreach (var profile in Monitors.SelectMany(monitor => monitor.Profiles))
                UnregisterProfileHotkey(profile);
            _eventAggregator.Unsubscribe(this);
        }
        await base.OnDeactivateAsync(close, cancellationToken);
    }
}
