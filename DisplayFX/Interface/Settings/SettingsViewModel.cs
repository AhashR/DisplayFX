using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Caliburn.Micro;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.ProfileSettings;
using DisplayFX.Objects.Entities;
using Microsoft.Win32;

namespace DisplayFX.Interface.Settings;

public class SettingsViewModel : Screen
{
    private readonly Computer _computer;
    private readonly DataController _dataController;
    private readonly RegistryController _registryController;
    private readonly BrightnessAutomationController? _automation;
    private readonly Computer _brightnessDraft;
    private string _settingsStatus = "";
    private bool? _recordingIncrease;
    private int _schedulePage;
    public const int SchedulePageSize = 3;
    public string? RestoreFilePath { get; private set; }

    public SettingsViewModel(
        Computer computer,
        RegistryController registryController,
        DataController dataController,
        BrightnessAutomationController? automation = null)
    {
        _computer = computer;
        _registryController = registryController;
        _dataController = dataController;
        _automation = automation;
        _brightnessDraft = new Computer { Monitors = computer.Monitors };
        CopyBrightnessSettings(computer, _brightnessDraft);
        BrightnessSchedules = new ObservableCollection<BrightnessSchedule>(_brightnessDraft.BrightnessSchedules);
        BrightnessSchedules.CollectionChanged += (_, _) => RefreshSchedulePages();
        if (_automation != null) _automation.StatusChanged += OnAutomationStatusChanged;
    }

    public bool IsStartWithWindows
    {
        get => _computer.IsStartWithWindows;
        set
        {
            if (value == _computer.IsStartWithWindows) return;
            SaveOption(() => _computer.IsStartWithWindows, newValue => _computer.IsStartWithWindows = newValue,
                value, nameof(IsStartWithWindows), true);
        }
    }

    public bool IsStartMinimized
    {
        get => _computer.IsStartMinimized;
        set
        {
            if (value == _computer.IsStartMinimized) return;
            SaveOption(() => _computer.IsStartMinimized, newValue => _computer.IsStartMinimized = newValue,
                value, nameof(IsStartMinimized));
        }
    }

    public bool IsMinimizeToTray
    {
        get => _computer.IsMinimizeToTray;
        set
        {
            if (value == _computer.IsMinimizeToTray) return;
            SaveOption(() => _computer.IsMinimizeToTray, newValue => _computer.IsMinimizeToTray = newValue,
                value, nameof(IsMinimizeToTray));
        }
    }

    public bool IsApplySettingsOnStart
    {
        get => _computer.IsApplySettingsOnStart;
        set
        {
            if (value == _computer.IsApplySettingsOnStart) return;
            SaveOption(() => _computer.IsApplySettingsOnStart, newValue => _computer.IsApplySettingsOnStart = newValue,
                value, nameof(IsApplySettingsOnStart));
        }
    }

    public bool IsRememberBrightnessOnStart
    {
        get => _computer.IsRememberBrightnessOnStart;
        set
        {
            if (value == _computer.IsRememberBrightnessOnStart) return;
            SaveOption(() => _computer.IsRememberBrightnessOnStart, newValue => _computer.IsRememberBrightnessOnStart = newValue,
                value, nameof(IsRememberBrightnessOnStart));
        }
    }

    public bool IsBrightnessShortcutsEnabled
    {
        get => _brightnessDraft.IsBrightnessShortcutsEnabled;
        set { _brightnessDraft.IsBrightnessShortcutsEnabled = value; NotifyOfPropertyChange(); }
    }
    public int BrightnessShortcutStep
    {
        get => _brightnessDraft.BrightnessShortcutStep;
        set { _brightnessDraft.BrightnessShortcutStep = value; NotifyOfPropertyChange(); }
    }
    public bool IsBrightnessScheduleEnabled
    {
        get => _brightnessDraft.IsBrightnessScheduleEnabled;
        set { _brightnessDraft.IsBrightnessScheduleEnabled = value; NotifyOfPropertyChange(); }
    }
    public ObservableCollection<BrightnessSchedule> BrightnessSchedules { get; }
    public IEnumerable<BrightnessSchedule> VisibleSchedules => BrightnessSchedules.Skip(_schedulePage * SchedulePageSize).Take(SchedulePageSize);
    public bool HasSchedulePages => BrightnessSchedules.Count > SchedulePageSize;
    public bool CanPreviousSchedulePage => _schedulePage > 0;
    public bool CanNextSchedulePage => (_schedulePage + 1) * SchedulePageSize < BrightnessSchedules.Count;
    public string SchedulePageText => $"{_schedulePage + 1} / {Math.Max(1, (BrightnessSchedules.Count + SchedulePageSize - 1) / SchedulePageSize)}";
    public void PreviousSchedulePage() { if (CanPreviousSchedulePage) { _schedulePage--; RefreshSchedulePages(); } }
    public void NextSchedulePage() { if (CanNextSchedulePage) { _schedulePage++; RefreshSchedulePages(); } }
    private void RefreshSchedulePages()
    {
        _schedulePage = Math.Min(_schedulePage, Math.Max(0, (BrightnessSchedules.Count - 1) / SchedulePageSize));
        foreach (var property in new[] { nameof(VisibleSchedules), nameof(HasSchedulePages), nameof(CanPreviousSchedulePage), nameof(CanNextSchedulePage), nameof(SchedulePageText) })
            NotifyOfPropertyChange(property);
    }
    public string IncreaseShortcutText => _recordingIncrease == true ? "Press keys (Esc cancels)" :
        ProfileSettingViewModel.FormatHotkey(_brightnessDraft.BrightnessIncreaseModifiers, _brightnessDraft.BrightnessIncreaseKey);
    public string DecreaseShortcutText => _recordingIncrease == false ? "Press keys (Esc cancels)" :
        ProfileSettingViewModel.FormatHotkey(_brightnessDraft.BrightnessDecreaseModifiers, _brightnessDraft.BrightnessDecreaseKey);
    public string ShortcutStatus => _automation?.ShortcutStatus ?? "Brightness shortcuts are off.";
    public string AutomationStatus => _automation?.Status ?? "Brightness automation is off.";
    public string SettingsStatus
    {
        get => _settingsStatus;
        private set { _settingsStatus = value; NotifyOfPropertyChange(); }
    }

    public void BeginRecording(bool increase)
    {
        _recordingIncrease = increase;
        NotifyShortcuts();
    }

    public bool RecordKey(Key key, ModifierKeys modifiers)
    {
        if (!_recordingIncrease.HasValue) return false;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return true;
        if (key != Key.Escape)
        {
            try { _ = new KeyGesture(key, modifiers); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException)
            { SettingsStatus = "Use Ctrl, Alt or Win with a key, or a function key."; return true; }
            if (_recordingIncrease.Value)
            { _brightnessDraft.BrightnessIncreaseKey = key; _brightnessDraft.BrightnessIncreaseModifiers = modifiers; }
            else
            { _brightnessDraft.BrightnessDecreaseKey = key; _brightnessDraft.BrightnessDecreaseModifiers = modifiers; }
        }
        _recordingIncrease = null;
        SettingsStatus = "Shortcut edited. Save brightness automation to apply it.";
        NotifyShortcuts();
        return true;
    }

    private void NotifyShortcuts()
    {
        NotifyOfPropertyChange(nameof(IncreaseShortcutText));
        NotifyOfPropertyChange(nameof(DecreaseShortcutText));
    }

    public void AddSchedule()
    {
        if (BrightnessSchedules.Count >= 100) { SettingsStatus = "You can save up to 100 daily brightness times."; return; }
        var time = Enumerable.Range(0, 24).Select(hour => $"{hour:00}:00")
            .FirstOrDefault(candidate => BrightnessSchedules.All(schedule => schedule.Time != candidate)) ?? "20:30";
        BrightnessSchedules.Add(new BrightnessSchedule { Time = time, Brightness = 40 });
        _schedulePage = (BrightnessSchedules.Count - 1) / SchedulePageSize;
        RefreshSchedulePages();
    }

    public void RemoveSchedule(BrightnessSchedule schedule) => BrightnessSchedules.Remove(schedule);

    public void SaveBrightnessAutomation()
    {
        _brightnessDraft.BrightnessSchedules = BrightnessSchedules.ToList();
        var error = ValidateBrightnessSettings(_brightnessDraft);
        if (error != null) { SettingsStatus = error; return; }
        var previous = new Computer();
        CopyBrightnessSettings(_computer, previous);
        CopyBrightnessSettings(_brightnessDraft, _computer);
        try
        {
            _dataController.Write(_computer);
            _automation?.SettingsChanged();
            SettingsStatus = "Brightness settings saved.";
        }
        catch (Exception ex)
        {
            CopyBrightnessSettings(previous, _computer);
            SettingsStatus = "Could not save brightness automation. " + ex.Message;
        }
    }
    public void ReportInputError() => SettingsStatus = "Enter a whole number from 1 to 100 for the step and 0 to 100 for brightness.";

    public void BackupSettings()
    {
        var dialog = new SaveFileDialog { Filter = "DisplayFX settings (*.json)|*.json", FileName = "DisplayFX-settings.json", DefaultExt = ".json" };
        if (dialog.ShowDialog() != true) return;
        try { ProfileTransferController.ExportSettings(_computer, dialog.FileName); SettingsStatus = "Backup saved."; }
        catch (Exception ex) { SettingsStatus = "Could not back up settings. " + ex.Message; }
    }
    public async Task RestoreSettings()
    {
        var dialog = new OpenFileDialog { Filter = "DisplayFX settings (*.json)|*.json", Title = "Restore settings" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            // Validate before closing this dialog; the shell commits and rebuilds the live models.
            ProfileTransferController.MergeSettings(_computer, ProfileTransferController.ReadSettings(dialog.FileName));
            RestoreFilePath = dialog.FileName;
            await TryCloseAsync(true);
        }
        catch (Exception ex) { SettingsStatus = "Could not restore settings. " + ex.Message; }
    }

    public static string? ValidateBrightnessSettings(Computer computer)
    {
        if (computer.BrightnessShortcutStep is < 1 or > 100) return "Brightness shortcut step must be between 1 and 100%.";
        if (computer.BrightnessSchedules == null || computer.BrightnessSchedules.Count > 100 ||
            computer.BrightnessSchedules.Any(schedule => schedule == null || !schedule.IsValid))
            return "Each daily time needs HH:mm (00:00 to 23:59) and brightness from 0 to 100%.";
        if (computer.BrightnessSchedules.GroupBy(schedule => schedule.Time).Any(group => group.Count() > 1))
            return "Daily brightness times must be unique.";
        if (computer.IsBrightnessScheduleEnabled && computer.BrightnessSchedules.Count == 0)
            return "Add at least one daily time before enabling the schedule.";
        return computer.IsBrightnessShortcutsEnabled ? BrightnessAutomationController.GetShortcutValidationError(computer, true) ??
            BrightnessAutomationController.GetShortcutValidationError(computer, false) : null;
    }

    private static void CopyBrightnessSettings(Computer source, Computer target)
    {
        target.IsBrightnessShortcutsEnabled = source.IsBrightnessShortcutsEnabled;
        target.BrightnessShortcutStep = source.BrightnessShortcutStep;
        target.BrightnessIncreaseModifiers = source.BrightnessIncreaseModifiers;
        target.BrightnessDecreaseModifiers = source.BrightnessDecreaseModifiers;
        target.BrightnessIncreaseKey = source.BrightnessIncreaseKey;
        target.BrightnessDecreaseKey = source.BrightnessDecreaseKey;
        target.IsBrightnessScheduleEnabled = source.IsBrightnessScheduleEnabled;
        target.BrightnessSchedules = (source.BrightnessSchedules ?? new()).Select(schedule =>
            new BrightnessSchedule { Time = schedule.Time, Brightness = schedule.Brightness }).ToList();
    }

    private void SaveOption(Func<bool> get, Action<bool> set, bool value, string property, bool startup = false)
    {
        var previous = get();
        try
        {
            if (startup) _registryController.RegisterForStartWithWindows(value);
            set(value);
            _dataController.Write(_computer);
            SettingsStatus = "Setting saved.";
        }
        catch (Exception ex)
        {
            set(previous);
            if (startup)
            {
                try { _registryController.RegisterForStartWithWindows(previous); }
                catch (Exception rollback) { SettingsStatus = "Startup preference could not be restored. " + rollback.Message; NotifyOfPropertyChange(property); return; }
            }
            SettingsStatus = "Could not save setting. " + ex.Message;
        }
        NotifyOfPropertyChange(property);
    }

    private void OnAutomationStatusChanged()
    {
        NotifyOfPropertyChange(nameof(ShortcutStatus));
        NotifyOfPropertyChange(nameof(AutomationStatus));
    }

    public Task Close() => TryCloseAsync(true);
    public Task Done() => Close();

    protected override Task OnDeactivateAsync(bool close, CancellationToken cancellationToken)
    {
        if (close && _automation != null) _automation.StatusChanged -= OnAutomationStatusChanged;
        return base.OnDeactivateAsync(close, cancellationToken);
    }
}
