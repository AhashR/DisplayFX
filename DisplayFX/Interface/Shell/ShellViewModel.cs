using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
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
    private readonly DisplayController _displayController;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger _logger;
    private readonly MonitorViewModelFactory _monitorViewModelFactory;
    private readonly ProcessController _processController;

    private readonly DisplayWindowManager _nvidiaDisplayWindowManager;
    private readonly ProfileFactory _profileFactory;
    private readonly IProfileViewModelFactory _profileViewModelFactory;

    private readonly RegistryController _registryController;
    private Computer _computer = null!;
    private ObservableCollection<MonitorViewModel> _monitors = null!;
    private List<Display>? _nvidiaDisplays;
    private readonly string _displayName;
    private bool _profileSettingsIsDirty;
    private MonitorViewModel? _selectedMonitor;
    private Display? _selectedNvidiaMonitor;
    private ProfileViewModel? _selectedProfile;
    private HotkeyManager? _hotkeyManager;
    private readonly Dictionary<int, ProfileViewModel> _hotkeyToProfile = new();
    private DispatcherTimer? _processTimer;

    public ShellViewModel(
        IEventAggregator eventAggregator,
        MonitorViewModelFactory monitorViewModelFactory,
        DataController dataController,
        IProfileViewModelFactory profileViewModelFactory,
        ProfileFactory profileFactory,
        ILogger logger,
        DisplayController displayController,
        DisplayWindowManager nvidiaDisplayWindowManager,
        RegistryController registryController,
        ProcessController processController)
    {
        _eventAggregator = eventAggregator;
        _monitorViewModelFactory = monitorViewModelFactory;
        _dataController = dataController;
        _profileViewModelFactory = profileViewModelFactory;
        _profileFactory = profileFactory;
        _logger = logger;
        _displayController = displayController;
        _nvidiaDisplayWindowManager = nvidiaDisplayWindowManager;
        _registryController = registryController;
        _processController = processController;

        var gpus = PhysicalGPU.GetPhysicalGPUs();
        _displayName = gpus.Length > 0 ? $"Adjust Displays - ({gpus[0].FullName})" : "Adjust Displays";

        _eventAggregator.SubscribeOnPublishedThread(this);

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

    public void OpenSettings()
    {
        _nvidiaDisplayWindowManager.OpenSettings(Computer);
        Write();
        NotifyOfPropertyChange(nameof(IsApplySettingsOnStart));
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

    private static string PaypalLink => "https://www.paypal.com/donate/?hosted_button_id=FT6HS8V8R4XYC";

    public Task HandleAsync(ProfileSettingsEvent message, CancellationToken cancellationToken)
    {
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
                Computer.Monitors.ForEach(BuildMonitorViewModel);
                LoadNvidiaDisplays();
                SelectPrimaryMonitorByDefault();
                ApplySettingsOnStart();
                StartProcessMonitoring();
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

                Monitors.Add(monitorViewModel);
            });
    }

    private void LoadNvidiaDisplays()
    {
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
            var activeProfile = monitorViewModel.Profiles.SingleOrDefault(p => p.IsActive);
            var nvidiaDisplay = _nvidiaDisplays?.SingleOrDefault(d => d.Name == monitorViewModel.ScreenName);

            if (activeProfile is not null)
                _displayController.UpdateColorSettings(
                    monitorViewModel.Display,
                    activeProfile.ProfileSettings!.ProfileSetting,
                    nvidiaDisplay);
        }
    }

    private IntPtr _lastForegroundHwnd;

    private void StartProcessMonitoring()
    {
        _processTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _processTimer.Tick -= OnProcessTimerTick;
        _processTimer.Tick += OnProcessTimerTick;
        _processTimer.Start();
    }

    private void OnProcessTimerTick(object? sender, EventArgs e)
    {
        var currentHwnd = ProcessController.GetForegroundWindow();
        if (currentHwnd == IntPtr.Zero || currentHwnd == _lastForegroundHwnd)
            return;

        _lastForegroundHwnd = currentHwnd;
        var foregroundExecutablePath = _processController.GetForegroundExecutablePath(currentHwnd);
        if (!string.IsNullOrWhiteSpace(foregroundExecutablePath))
            ApplyLinkedProfiles(NormalizeExecutablePath(foregroundExecutablePath));
    }

    private void ApplyLinkedProfiles(string foregroundExecutablePath)
    {
        foreach (var monitor in Monitors)
        {
            var linkedProfiles = monitor.Profiles
                .Where(profile => !string.IsNullOrWhiteSpace(profile.Profile.LinkedExecutablePath))
                .ToList();

            var matchedProfile = linkedProfiles
                .FirstOrDefault(profile => IsExecutableRunning(profile.Profile.LinkedExecutablePath, foregroundExecutablePath));

            var targetProfile = matchedProfile ?? monitor.Profiles.FirstOrDefault(profile => profile.IsDefault);
            if (targetProfile is null || targetProfile.IsActive)
                continue;

            ApplyProfile(targetProfile, false);
        }
    }

    private static bool IsExecutableRunning(string? linkedExecutablePath, string foregroundExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(linkedExecutablePath))
            return false;

        try
        {
            var linkedFullPath = NormalizeExecutablePath(linkedExecutablePath);
            return string.Equals(linkedFullPath, foregroundExecutablePath, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       Path.GetFileName(linkedFullPath),
                       Path.GetFileName(foregroundExecutablePath),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeExecutablePath(string executablePath)
    {
        return Path.GetFullPath(executablePath.Trim());
    }

    private void WireProfileEvents(ProfileViewModel profileViewModel)
    {
        profileViewModel.IsSelectedChanged += OnProfileViewModelSelectedChanged;
        profileViewModel.ProfileRemoved += OnProfileRemoved;
        
        // Register hotkey when profile is created/loaded
        RegisterProfileHotkey(profileViewModel);
    }

    private void OnProfileViewModelSelectedChanged(Guid guid, bool value)
    {
        if (value)
        {
            SelectedProfile = SelectedMonitor!.Profiles.Single(p => p.Guid == guid);
            foreach (var profileViewModel in SelectedMonitor.Profiles.Where(p => p.Guid != guid))
                profileViewModel.UnSelect();
        }
        else
        {
            foreach (var profileViewModel in SelectedMonitor!.Profiles)
                profileViewModel.UnSelect();
        }

        ProfileSettingsIsDirty = false;
        NotifyOfPropertyChange(nameof(CanApply));
    }

    private void OnProfileRemoved(Guid guid)
    {
        var profileViewModel = SelectedMonitor!.Profiles.Single(p => p.Guid == guid);

        // Unregister hotkey before removing profile
        UnregisterProfileHotkey(profileViewModel);

        SelectedMonitor.Monitor.Profiles.Remove(profileViewModel.Profile);
        SelectedMonitor?.Profiles.Remove(profileViewModel);

        NotifyOfPropertyChange(nameof(CanAddProfile));
        NotifyOfPropertyChange(nameof(ProfileGroupBoxText));

        Write();
    }

    private void Write()
    {
        _dataController.Write(Computer);
    }

    private void OnMonitorViewModelIsSelectedChanged(bool isSelected, Guid selectedMonitor)
    {
        if (SelectedMonitor != null) 
            SelectedMonitor.IsSelected = false;
        
        SelectedMonitor = isSelected ? Monitors.Single(m => m.Guid == selectedMonitor) : null;
        SelectedNvidiaMonitor = _nvidiaDisplays?.SingleOrDefault(d => d.Name == SelectedMonitor?.ScreenName);

        SetSelectedProfile();
    }

    private void SetSelectedProfile()
    {
        SelectedProfile = SelectedMonitor?.Profiles.SingleOrDefault(p => p.IsActive);
        if (SelectedProfile is not null)
            SelectedProfile.IsSelected = true;
    }

    public void AddProfile()
    {
        _nvidiaDisplayWindowManager
            .OpenProfileNameViewModel()
            .IfSuccess(profileName =>
            {
                var profile = _profileFactory.Create(SelectedMonitor!.Monitor, profileName);
                var profileViewModel = _profileViewModelFactory.Create(profile, SelectedMonitor);

                WireProfileEvents(profileViewModel);

                SelectedMonitor?.Profiles.Add(profileViewModel);
                SelectedProfile = profileViewModel;
                SelectedProfile.IsSelected = true;

                NotifyOfPropertyChange(nameof(CanAddProfile));
                NotifyOfPropertyChange(nameof(ProfileGroupBoxText));

                Write();
            });
    }

    public void Apply()
    {
        ApplyProfile(SelectedProfile!);

        ProfileSettingsIsDirty = false;
    }

    private void SetActiveProfile()
    {
        SelectedProfile!.IsActive = true;
        foreach (var profileViewModel in SelectedMonitor!.Profiles.Where(p => p.Guid != SelectedProfile.Guid))
            profileViewModel.Deactivate();
    }

    public void Revert()
    {
        Task.Run(async () => await _eventAggregator.PublishOnCurrentThreadAsync(new RevertEvent()));
    }

    public void Update()
    {
        Write();

        SelectedProfile!.IsUpdated();
        ProfileSettingsIsDirty = false;
        
        // Re-register hotkey in case it changed
        ReregisterProfileHotkey(SelectedProfile);
    }

    public void OpenHelp()
    {
        _nvidiaDisplayWindowManager.OpenHelp();
    }

    public void OpenDonation()
    {
        _nvidiaDisplayWindowManager.OpenWebsite(PaypalLink);
    }

    protected override void OnViewLoaded(object view)
    {
        base.OnViewLoaded(view);
        InitializeHotkeyManager(view);
    }

    private void InitializeHotkeyManager(object view)
    {
        if (view is Window window)
        {
            var helper = new WindowInteropHelper(window);
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
        if (profileViewModel.Profile.HotkeyModifiers.HasValue && 
            profileViewModel.Profile.HotkeyKey.HasValue &&
            _hotkeyManager != null)
        {
            try
            {
                var keyGesture = new KeyGesture(
                    profileViewModel.Profile.HotkeyKey.Value,
                    profileViewModel.Profile.HotkeyModifiers.Value);
                
                string hotkeyID = profileViewModel.Guid.ToString();
                _hotkeyManager.AddOrReplace(
                    profileViewModel.Name, 
                    keyGesture, 
                    (s, e) => ActivateProfile(profileViewModel)); 

                _hotkeyToProfile[profileViewModel.Guid.ToString().GetHashCode()] = profileViewModel;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to register hotkey for profile: {profileViewModel.Name}. Error: {ex.Message}");
            }
        }
    }

    private void UnregisterProfileHotkey(ProfileViewModel profileViewModel)
    {
        if (_hotkeyManager != null)
        {
            _hotkeyManager.Remove(profileViewModel.Guid.ToString());
            
            var hashCode = profileViewModel.Guid.ToString().GetHashCode();
            _hotkeyToProfile.Remove(hashCode);
        }
    }

    private void ReregisterProfileHotkey(ProfileViewModel profileViewModel)
    {
        UnregisterProfileHotkey(profileViewModel);
        RegisterProfileHotkey(profileViewModel);
    }

    private void ActivateProfile(ProfileViewModel profileViewModel)
    {
        ApplyProfile(profileViewModel);
    }

    private void ApplyProfile(ProfileViewModel profileViewModel, bool updateSelection = true)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                var monitor = Monitors.FirstOrDefault(m => m.Profiles.Contains(profileViewModel));
                if (monitor != null)
                {
                    var nvidiaDisplay = _nvidiaDisplays?.SingleOrDefault(d => d.Name == monitor.ScreenName);
                    
                    _displayController.UpdateColorSettings(
                        monitor.Display,
                        profileViewModel.Profile.ProfileSetting, 
                        nvidiaDisplay);

                    profileViewModel.IsActive = true;
                    foreach (var otherProfile in monitor.Profiles.Where(p => p.Guid != profileViewModel.Guid))
                    {
                        otherProfile.Deactivate();
                    }

                    if (updateSelection)
                    {
                        SelectedMonitor = monitor;
                        SelectedProfile = profileViewModel;
                    }

                    Write(); // Save the state
                    GlobalEvents.UpdateToolTip.Invoke();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error applying profile");
            }
        });
    }
}