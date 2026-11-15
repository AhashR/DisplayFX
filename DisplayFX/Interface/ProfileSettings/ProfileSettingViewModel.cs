using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Caliburn.Micro;
using Microsoft.Win32;
using System.IO;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.HandleEvents;

namespace DisplayFX.Interface.ProfileSettings;

public partial class ProfileSettingViewModel : Screen, IHandle<RevertEvent>
{
    private readonly IEventAggregator _eventAggregator;
    private readonly Profile _profile;
    private ProfileSetting _originalSettings = null!;
    private ModifierKeys? _originalHotkeyModifiers;
    private Key? _originalHotkeyKey;
    private ModifierKeys? _hotkeyModifiers;
    private Key? _hotkeyKey;
    private string? _originalExecutablePath;
    private string? _linkedExecutablePath;
    private bool _resetting;
    private bool _isRecordingHotkey;
    private string? _hotkeyStatus;

    public ProfileSettingViewModel(ProfileSetting profileSetting, bool isDefault,
        IEventAggregator eventAggregator, Profile profile)
    {
        _profile = profile;
        _eventAggregator = eventAggregator;
        ProfileSetting = profileSetting.Copy();
        _hotkeyModifiers = profile.HotkeyModifiers;
        _hotkeyKey = profile.HotkeyKey;
        _linkedExecutablePath = profile.LinkedExecutablePath;
        IsDefault = isDefault;

        SetOriginalSettings(profileSetting);
        _eventAggregator.SubscribeOnUIThread(this);
    }

    public ProfileSetting ProfileSetting { get; }

    public bool IsDefault { get; }
    public bool CanEdit => true;
    public bool IsDirty =>
        ProfileSetting.Brightness != _originalSettings.Brightness ||
        ProfileSetting.Contrast != _originalSettings.Contrast ||
        ProfileSetting.Gamma != _originalSettings.Gamma ||
        ProfileSetting.DigitalVibrance != _originalSettings.DigitalVibrance ||
        ProfileSetting.RedGain != _originalSettings.RedGain || ProfileSetting.GreenGain != _originalSettings.GreenGain || ProfileSetting.BlueGain != _originalSettings.BlueGain || ProfileSetting.TemperatureKelvin != _originalSettings.TemperatureKelvin ||
        _hotkeyModifiers != _originalHotkeyModifiers ||
        _hotkeyKey != _originalHotkeyKey ||
        !string.Equals(_linkedExecutablePath, _originalExecutablePath, StringComparison.OrdinalIgnoreCase);

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

    public string? HotkeyStatus => _hotkeyStatus;
    public bool HasHotkeyStatus => !string.IsNullOrWhiteSpace(_hotkeyStatus);

    public void SetHotkeyStatus(string? message)
    {
        if (string.Equals(_hotkeyStatus, message, StringComparison.Ordinal)) return;
        _hotkeyStatus = message;
        NotifyOfPropertyChange(nameof(HotkeyStatus));
        NotifyOfPropertyChange(nameof(HasHotkeyStatus));
    }

    public string HotkeyDisplayText
    {
        get
        {
            if (IsRecordingHotkey)
                return DisplayFX.Global.Controllers.LocalizationController.Instance["Press key combination..."];

            return FormatHotkey(_hotkeyModifiers, _hotkeyKey);
        }
    }

    public static string FormatHotkey(ModifierKeys? modifiers, Key? key)
    {
        if (!key.HasValue || key.Value == Key.None)
            return DisplayFX.Global.Controllers.LocalizationController.Instance["Click to record shortcut"];

        var parts = new List<string>();
        if (modifiers.HasValue)
        {
            if (modifiers.Value.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.Value.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (modifiers.Value.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (modifiers.Value.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        }

        var keyName = key.Value switch
        {
            Key.PageUp => "Page Up",
            Key.PageDown => "Page Down",
            Key.Return => "Enter",
            _ => key.Value.ToString()
        };
        if (key.Value != Key.LeftCtrl && key.Value != Key.RightCtrl &&
            key.Value != Key.LeftAlt && key.Value != Key.RightAlt &&
            key.Value != Key.LeftShift && key.Value != Key.RightShift &&
            key.Value != Key.LWin && key.Value != Key.RWin)
        {
            if (keyName.Length == 2 && keyName.StartsWith("D") && char.IsDigit(keyName[1]))
                keyName = keyName.Substring(1);

            parts.Add(keyName);
        }

        return parts.Count > 0 ? string.Join(" + ", parts) : DisplayFX.Global.Controllers.LocalizationController.Instance["Click to record shortcut"];
    }

    public void SetRecordedHotkey(ModifierKeys? modifiers, Key key)
    {
        _resetting = true;
        _hotkeyModifiers = modifiers == ModifierKeys.None ? null : modifiers;
        _hotkeyKey = key;
        _resetting = false;

        _isRecordingHotkey = false;
        NotifyOfPropertyChange(nameof(IsRecordingHotkey));
        NotifyOfPropertyChange(nameof(HotkeyDisplayText));
        Publish();
    }

    public ICommand ClearHotkeyCommand => new RelayCommand(ClearHotkey);
    public ICommand BrowseExecutableCommand => new RelayCommand(BrowseExecutable);
    public ICommand ClearExecutableCommand => new RelayCommand(() => LinkedExecutablePath = null);
    public string? LinkedExecutablePath
    {
        get => _linkedExecutablePath;
        set
        {
            var path = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(path, _linkedExecutablePath, StringComparison.OrdinalIgnoreCase)) return;
            _linkedExecutablePath = path;
            NotifyOfPropertyChange();
            NotifyOfPropertyChange(nameof(LinkedApplicationName));
            Publish();
        }
    }
    public string LinkedApplicationName => string.IsNullOrWhiteSpace(LinkedExecutablePath)
        ? DisplayFX.Global.Controllers.LocalizationController.Instance["Link application"] : Path.GetFileNameWithoutExtension(LinkedExecutablePath);
    private void BrowseExecutable()
    {
        var dialog = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe", Title = "Link application" };
        if (dialog.ShowDialog() == true) LinkedExecutablePath = dialog.FileName;
    }
    public double Brightness
    {
        get => ProfileSetting.Brightness;
        set
        {
            if (value.Equals(ProfileSetting.Brightness)) return;
            TrackColorEdit();
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
            TrackColorEdit();
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
            TrackColorEdit();
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
            TrackColorEdit();
            ProfileSetting.DigitalVibrance = value;
            NotifyOfPropertyChange();
            Publish();
        }
    }

    public Task HandleAsync(RevertEvent message, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(message.Profile, _profile))
            return Task.CompletedTask;

        _resetting = true;
        {
            Brightness = _originalSettings.Brightness;
            Contrast = _originalSettings.Contrast;
            Gamma = _originalSettings.Gamma;
            DigitalVibrance = _originalSettings.DigitalVibrance;
            RedGain = _originalSettings.RedGain; GreenGain = _originalSettings.GreenGain; BlueGain = _originalSettings.BlueGain; TemperatureKelvin = _originalSettings.TemperatureKelvin;
            LinkedExecutablePath = _originalExecutablePath;
            _hotkeyModifiers = _originalHotkeyModifiers;
            _hotkeyKey = _originalHotkeyKey;
            IsRecordingHotkey = false;
        }
        _resetting = false;

        NotifyOfPropertyChange(nameof(HotkeyDisplayText));
        Publish();

        return Task.CompletedTask;
    }

    private void ClearHotkey()
    {
        _resetting = true;
        _hotkeyModifiers = null;
        _hotkeyKey = null;
        _isRecordingHotkey = false;
        _resetting = false;

        NotifyOfPropertyChange(nameof(IsRecordingHotkey));
        NotifyOfPropertyChange(nameof(HotkeyDisplayText));

        Publish();
    }

    private void SetOriginalSettings(ProfileSetting profileSetting)
    {
        _originalSettings = profileSetting.Copy();
        _originalHotkeyModifiers = _profile.HotkeyModifiers;
        _originalHotkeyKey = _profile.HotkeyKey;
        _originalExecutablePath = _profile.LinkedExecutablePath;
    }

    private void Publish()
    {
        if (!_resetting)
        {
            NotifyOfPropertyChange(nameof(IsDirty));
            NotifyColorInputs();
            _ = _eventAggregator.PublishOnUIThreadAsync(new ProfileSettingsEvent(_profile, IsDirty));
        }
    }

    private void IsUpdated()
    {
        SetOriginalSettings(ProfileSetting);
        Publish();
    }

    /// <summary>Commits drafts only while persisting succeeds, leaving unrelated saves isolated from edits.</summary>
    public void Save(System.Action persist)
    {
        if (HasInputErrors) throw new InvalidOperationException(InputError);
        var savedSettings = _profile.ProfileSetting;
        var previousSettings = savedSettings.Copy();
        var previousModifiers = _profile.HotkeyModifiers;
        var previousKey = _profile.HotkeyKey;
        var previousPath = _profile.LinkedExecutablePath;
        var previousModified = _profile.ModifiedAt;

        CopySettings(ProfileSetting, savedSettings);
        _profile.HotkeyModifiers = _hotkeyModifiers;
        _profile.HotkeyKey = _hotkeyKey;
        _profile.LinkedExecutablePath = _linkedExecutablePath;
        _profile.ModifiedAt = DateTimeOffset.UtcNow;
        try
        {
            if (DisplayFX.Global.Controllers.ProfileTransferController.GetExecutableConflict(_profile) is string conflict)
                throw new InvalidOperationException(conflict);
            persist();
        }
        catch
        {
            CopySettings(previousSettings, savedSettings);
            _profile.HotkeyModifiers = previousModifiers;
            _profile.HotkeyKey = previousKey;
            _profile.LinkedExecutablePath = previousPath;
            _profile.ModifiedAt = previousModified;
            throw;
        }

        IsUpdated();
    }

    private static void CopySettings(ProfileSetting source, ProfileSetting target)
    {
        target.Brightness = source.Brightness;
        target.Contrast = source.Contrast;
        target.Gamma = source.Gamma;
        target.DigitalVibrance = source.DigitalVibrance;
        target.RedGain = source.RedGain; target.GreenGain = source.GreenGain; target.BlueGain = source.BlueGain; target.TemperatureKelvin = source.TemperatureKelvin;
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

    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
