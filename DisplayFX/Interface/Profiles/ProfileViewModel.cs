using System;
using System.Windows;
using System.Windows.Controls;
using Caliburn.Micro;
using DisplayFX.Interface.Monitors;
using DisplayFX.Interface.ProfileSettings;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.Factories.Interfaces;

namespace DisplayFX.Interface.Profiles;

public class ProfileViewModel : Screen
{
    private readonly IProfileSettingViewModelFactory _profileSettingViewModelFactory;
    private bool _callEvent;
    private bool _isSelected;
    private ProfileSettingViewModel? _profileSettings;

    public ProfileViewModel(Profile profile, MonitorViewModel monitorViewModel,
        IProfileSettingViewModelFactory profileSettingViewModelFactory)
    {
        _profileSettingViewModelFactory = profileSettingViewModelFactory;
        Profile = profile;
        MonitorViewModel = monitorViewModel;

        Start();
    }

    public Profile Profile { get; }
    public Action<Guid> ProfileRemoved { get; set; } = null!;
    public Action<Guid>? ProfileDuplicateRequested { get; set; }
    public Action<Guid>? ProfileRenameRequested { get; set; }
    public Action<Guid, int>? ProfileMoveRequested { get; set; }
    public string Name => Profile.Name;
    public string Icon => ProfileIcons.Glyph(Profile.Icon);
    public Action<ProfileViewModel, string>? IconRequested { get; set; }
    public Action<ProfileViewModel>? ClearShortcutRequested { get; set; }
    public Guid Guid { get; set; }

    public ProfileSettingViewModel? ProfileSettings
    {
        get => _profileSettings;
        set
        {
            if (Equals(value, _profileSettings)) return;
            _profileSettings = value;
            NotifyOfPropertyChange();
        }
    }

    public ContextMenu ContextMenu { get; set; } = null!;
    public MonitorViewModel MonitorViewModel { get; set; }

    public new bool IsActive
    {
        get => Profile.IsActive;
        set
        {
            if (value == Profile.IsActive) return;
            Profile.IsActive = value;
            NotifyOfPropertyChange();
            }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value == _isSelected) return;
            _isSelected = value;
            NotifyOfPropertyChange();
            if (_callEvent)
            {
                IsSelectedChanged?.Invoke(Guid, value);
            }
        }
    }

    public Action<Guid, bool> IsSelectedChanged { get; set; } = null!;
    public bool IsDefault => Profile.IsDefault;

    private void Start()
    {
        Guid = Guid.NewGuid();
        _callEvent = true;

        CreateContextMenu();
        BuildProfileSettings();
    }

    private void BuildProfileSettings()
    {
        ProfileSettings = _profileSettingViewModelFactory
            .Create(Profile.ProfileSetting, Profile.IsDefault, Profile);
    }

    private void CreateContextMenu()
    {
        ContextMenu = new ContextMenu();
        ContextMenu.Opened += (_, _) => BuildMenu();
        BuildMenu();
    }

    private void BuildMenu()
    {
        ContextMenu.Items.Clear();
        var icons = new MenuItem { Header = "Choose icon" };
        foreach (var icon in ProfileIcons.Choices)
        {
            var option = new MenuItem { Header = icon.Label, IsCheckable = true, IsChecked = Profile.Icon == icon.Id,
                Icon = new TextBlock { Text = icon.Glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets") } };
            option.Click += (_, _) => IconRequested?.Invoke(this, icon.Id);
            icons.Items.Add(option);
        }
        ContextMenu.Items.Add(icons);
        ContextMenu.Items.Add(new Separator());
        if (Profile.HotkeyKey is not (null or System.Windows.Input.Key.None))
            AddMenuItem("Remove existing profile shortcut", () => ClearShortcutRequested?.Invoke(this));
        AddMenuItem("Move up", () => ProfileMoveRequested?.Invoke(Guid, -1));
        AddMenuItem("Move down", () => ProfileMoveRequested?.Invoke(Guid, 1));
        AddMenuItem("Duplicate", () => ProfileDuplicateRequested?.Invoke(Guid));
        if (Profile.IsDefault)
            return;

        AddMenuItem("Rename...", () => ProfileRenameRequested?.Invoke(Guid));
        ContextMenu.Items.Add(new Separator());
        var menuItem1 = new MenuItem
        {
            Header = "Remove"
        };
        menuItem1.Click += OnRemoveClicked;
        ContextMenu.Items.Add(menuItem1);
    }

    private void AddMenuItem(string header, System.Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        ContextMenu.Items.Add(item);
    }

    public void RefreshName() => NotifyOfPropertyChange(nameof(Name));
    public void RefreshMetadata()
    {
        if (!ContextMenu.IsOpen) BuildMenu();
        foreach (var property in new[] { nameof(Name), nameof(Icon) }) NotifyOfPropertyChange(property);
    }

    private void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        ProfileRemoved?.Invoke(Guid);
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void UnSelect()
    {
        _callEvent = false;
        {
            IsSelected = false;
        }
        _callEvent = true;
    }
}
