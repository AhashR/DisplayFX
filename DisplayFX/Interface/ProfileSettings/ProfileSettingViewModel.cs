using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Caliburn.Micro;
using Microsoft.Win32;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.HandleEvents;

namespace DisplayFX.Interface.ProfileSettings;

public class ProfileSettingViewModel : Screen, IHandle<RevertEvent>
{
    private readonly IEventAggregator _eventAggregator;
    private readonly Profile _profile;
    private ProfileSetting _originalSettings = null!;
    private bool _resetting;
    private bool _isRecordingHotkey;

    public ProfileSettingViewModel(ProfileSetting profileSetting, bool isDefault,
        IEventAggregator eventAggregator, Profile profile)
    {
        _profile = profile;
        _eventAggregator = eventAggregator;
        ProfileSetting = profileSetting;
        IsDefault = isDefault;

        SetOriginalSettings(profileSetting);
        _eventAggregator.SubscribeOnPublishedThread(this);
    }

    public ProfileSetting ProfileSetting { get; }

    public bool IsDefault { get; }
    public bool CanEdit => !IsDefault;

    public bool IsRecordingHotkey
    {
        get => _isRecordingHotkey;
        set
        {
            if (_isRecordingHotkey == value) return;
            _isRecordingHotkey = value;
            NotifyOfPropertyChange();
            NotifyOfPropertyChange(nameof(HotkeyDisplayText));
        }
    }

    public string HotkeyDisplayText
    {
        get
        {
            if (IsRecordingHotkey)
                return "Press key combination...";

            return FormatHotkey(_profile.HotkeyModifiers, _profile.HotkeyKey);
        }
    }

    public static string FormatHotkey(ModifierKeys? modifiers, Key? key)
    {
        if (!key.HasValue || key.Value == Key.None)
            return "Click to record shortcut";

        var parts = new List<string>();
        if (modifiers.HasValue)
        {
            if (modifiers.Value.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.Value.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (modifiers.Value.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        }

        var keyName = key.Value.ToString();
        if (key.Value != Key.LeftCtrl && key.Value != Key.RightCtrl &&
            key.Value != Key.LeftAlt && key.Value != Key.RightAlt &&
            key.Value != Key.LeftShift && key.Value != Key.RightShift)
        {
            if (keyName.Length == 2 && keyName.StartsWith("D") && char.IsDigit(keyName[1]))
                keyName = keyName.Substring(1);

            parts.Add(keyName);
        }

        return parts.Count > 0 ? string.Join(" + ", parts) : "Click to record shortcut";
    }

    public void SetRecordedHotkey(ModifierKeys? modifiers, Key key)
    {
        _resetting = true;
        _profile.HotkeyModifiers = modifiers == ModifierKeys.None ? null : modifiers;
        _profile.HotkeyKey = key;
        _resetting = false;

        _isRecordingHotkey = false;
        NotifyOfPropertyChange(nameof(IsRecordingHotkey));
        NotifyOfPropertyChange(nameof(HotkeyDisplayText));
        Publish();
    }

    public ICommand ClearHotkeyCommand => new RelayCommand(ClearHotkey);
    public ICommand BrowseExecutableCommand => new RelayCommand(BrowseExecutable);
    public ICommand ClearExecutableCommand => new RelayCommand(ClearExecutable);

    public string? LinkedExecutablePath
    {
        get => _profile.LinkedExecutablePath;
        set
        {
            var normalizedValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(normalizedValue, _profile.LinkedExecutablePath, StringComparison.OrdinalIgnoreCase)) return;

            _profile.LinkedExecutablePath = normalizedValue;
            NotifyOfPropertyChange();
            Publish();
        }
    }

    public double Brightness
    {
        get => ProfileSetting.Brightness;
        set
        {
            if (value.Equals(ProfileSetting.Brightness)) return;
            ProfileSetting.Brightness = value;
            NotifyOfPropertyChange();
            Publish();
        }
    }

    public double Contrast
    {
        get => ProfileSetting.Contrast;
        set
        {
            if (value.Equals(ProfileSetting.Contrast)) return;
            ProfileSetting.Contrast = value;
            NotifyOfPropertyChange();
            Publish();
        }
    }

    public double Gamma
    {
        get => ProfileSetting.Gamma;
        set
        {
            if (value.Equals(ProfileSetting.Gamma)) return;
            ProfileSetting.Gamma = value;
            NotifyOfPropertyChange();
            Publish();
        }
    }

    public double DigitalVibrance
    {
        get => ProfileSetting.DigitalVibrance;
        set
        {
            if (value.Equals(ProfileSetting.DigitalVibrance)) return;
            ProfileSetting.DigitalVibrance = value;
            NotifyOfPropertyChange();
            Publish();
        }
    }

    public Task HandleAsync(RevertEvent message, CancellationToken cancellationToken)
    {
        _resetting = true;
        {
            Brightness = _originalSettings.Brightness;
            Contrast = _originalSettings.Contrast;
            Gamma = _originalSettings.Gamma;
        }
        _resetting = false;

        Publish(false);

        return Task.CompletedTask;
    }

    private void ClearHotkey()
    {
        _resetting = true;
        _profile.HotkeyModifiers = null;
        _profile.HotkeyKey = null;
        _isRecordingHotkey = false;
        _resetting = false;

        NotifyOfPropertyChange(nameof(IsRecordingHotkey));
        NotifyOfPropertyChange(nameof(HotkeyDisplayText));

        Publish();
    }

    private void BrowseExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Select an executable to link"
        };

        if (dialog.ShowDialog() == true)
            LinkedExecutablePath = dialog.FileName;
    }

    private void ClearExecutable()
    {
        LinkedExecutablePath = null;
    }

    private void SetOriginalSettings(ProfileSetting profileSetting)
    {
        _originalSettings = new ProfileSetting(profileSetting.Brightness, profileSetting.Contrast,
            profileSetting.Gamma, profileSetting.DigitalVibrance);
    }

    private void Publish(bool value = true)
    {
        if (!_resetting)
            Task.Run(async () => await _eventAggregator.PublishOnCurrentThreadAsync(new ProfileSettingsEvent(value)));
    }

    public void IsUpdated()
    {
        SetOriginalSettings(ProfileSetting);
    }
}

public class RelayCommand : ICommand
{
    private readonly System.Action _execute;
    public RelayCommand(System.Action execute)
    {
        _execute = execute;
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        _execute();
    }

    public event EventHandler? CanExecuteChanged;
}
