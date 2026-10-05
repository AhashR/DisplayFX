using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using DisplayFX.Objects.Entities;
using NHotkey.Wpf;
using NLog;
using WindowsDisplayAPI;

namespace DisplayFX.Global.Controllers;

/// <summary>Opt-in global brightness shortcuts and daily local-time schedules.</summary>
public class BrightnessAutomationController : IDisposable
{
    private const string IncreaseHotkeyId = "DisplayFX.Brightness.Increase";
    private const string DecreaseHotkeyId = "DisplayFX.Brightness.Decrease";
    private readonly DisplayCache _displayCache;
    private readonly MonitorBrightnessController _brightnessController;
    private readonly BrightnessPersistenceController _persistence;
    private readonly ILogger _logger;
    private DispatcherTimer? _timer;
    private Computer? _computer;
    private bool _started;
    private bool _hotkeysSuspended;
    private DateTime _lastScheduleCheck;

    public BrightnessAutomationController(DisplayCache displayCache, MonitorBrightnessController brightnessController,
        BrightnessPersistenceController persistence, ILogger logger)
    {
        _displayCache = displayCache;
        _brightnessController = brightnessController;
        _persistence = persistence;
        _logger = logger;
    }

    public event Action? StatusChanged;
    public string Status { get; private set; } = "Brightness automation is off.";
    public string ShortcutStatus { get; private set; } = "Brightness shortcuts are off.";

    public void Attach(Computer computer)
    {
        Stop();
        _computer = computer;
    }

    public void Detach()
    {
        Stop();
        _computer = null;
    }

    public void Start()
    {
        if (_started || _computer is null)
            return;
        _started = true;
        _lastScheduleCheck = DateTime.Now;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
        UpdateScheduleStatus();
        RefreshHotkeys();
    }

    public void Stop()
    {
        _started = false;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }
        RemoveHotkeys();
    }

    public void Dispose() => Detach();

    /// <summary>Temporarily releases brightness shortcuts while their key combinations are recorded.</summary>
    public void SuspendHotkeys(bool suspended)
    {
        _hotkeysSuspended = suspended;
        RefreshHotkeys();
    }

    public void SettingsChanged()
    {
        // Starting/enabling or editing a schedule never applies a past occurrence.
        _lastScheduleCheck = DateTime.Now;
        UpdateScheduleStatus();
        RefreshHotkeys();
    }

    public void RefreshHotkeys()
    {
        RemoveHotkeys();
        if (_computer is null || !_computer.IsBrightnessShortcutsEnabled)
        {
            ShortcutStatus = "Brightness shortcuts are off.";
            NotifyStatus();
            return;
        }
        if (!_started || _hotkeysSuspended)
        {
            ShortcutStatus = _hotkeysSuspended ? "Shortcuts paused while settings are open." : "Shortcuts will activate when the app is ready.";
            NotifyStatus();
            return;
        }

        var increaseError = GetShortcutValidationError(_computer, true);
        var decreaseError = GetShortcutValidationError(_computer, false);
        if (increaseError is not null || decreaseError is not null)
        {
            ShortcutStatus = increaseError ?? decreaseError!;
            NotifyStatus();
            return;
        }

        try
        {
            RegisterHotkey(IncreaseHotkeyId, _computer.BrightnessIncreaseKey!.Value,
                _computer.BrightnessIncreaseModifiers ?? ModifierKeys.None, 1);
            RegisterHotkey(DecreaseHotkeyId, _computer.BrightnessDecreaseKey!.Value,
                _computer.BrightnessDecreaseModifiers ?? ModifierKeys.None, -1);
            ShortcutStatus = "Brightness shortcuts are active for all connected DDC/CI monitors.";
        }
        catch (Exception exception)
        {
            RemoveHotkeys();
            ShortcutStatus = "Brightness shortcut is unavailable or used by another app. Choose another combination.";
            _logger.Warn(exception, "Failed to register brightness shortcuts.");
        }
        NotifyStatus();
    }

    public static string? GetShortcutValidationError(Computer computer, bool increase)
    {
        var key = increase ? computer.BrightnessIncreaseKey : computer.BrightnessDecreaseKey;
        var modifiers = (increase ? computer.BrightnessIncreaseModifiers : computer.BrightnessDecreaseModifiers) ?? ModifierKeys.None;
        var label = increase ? "Increase" : "Decrease";
        if (!key.HasValue || key.Value == Key.None)
            return $"Choose an {label.ToLowerInvariant()} brightness shortcut.";
        try
        {
            _ = new KeyGesture(key.Value, modifiers);
        }
        catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is InvalidOperationException)
        {
            return $"{label} brightness shortcut needs a valid key with Ctrl, Alt or Win (or a function key).";
        }

        var otherKey = increase ? computer.BrightnessDecreaseKey : computer.BrightnessIncreaseKey;
        var otherModifiers = (increase ? computer.BrightnessDecreaseModifiers : computer.BrightnessIncreaseModifiers) ?? ModifierKeys.None;
        if (key == otherKey && modifiers == otherModifiers)
            return "Increase and decrease brightness need different shortcuts.";
        if (computer.Monitors.SelectMany(monitor => monitor.Profiles).Any(profile =>
                profile.HotkeyKey == key && (profile.HotkeyModifiers ?? ModifierKeys.None) == modifiers))
            return $"{label} brightness shortcut is also assigned to a profile. Choose another combination.";
        return null;
    }

    private void RegisterHotkey(string id, Key key, ModifierKeys modifiers, int direction)
    {
        HotkeyManager.Current.AddOrReplace(id, new KeyGesture(key, modifiers), (_, args) =>
        {
            if (_computer?.IsBrightnessShortcutsEnabled == true)
                AdjustBrightness(direction * Math.Clamp(_computer.BrightnessShortcutStep, 1, 100));
            args.Handled = true;
        });
    }

    private void RemoveHotkeys()
    {
        // Do not initialize the native hotkey window until the app has a dispatcher.
        if (System.Windows.Application.Current is null)
            return;
        try
        {
            HotkeyManager.Current.Remove(IncreaseHotkeyId);
            HotkeyManager.Current.Remove(DecreaseHotkeyId);
        }
        catch (Exception exception) { _logger.Warn(exception, "Could not release brightness shortcuts."); }
    }

    protected virtual IReadOnlyList<Display> GetConnectedDisplays()
    {
        _displayCache.Refresh();
        return _displayCache.GetDisplays();
    }

    /// <summary>Each display retains its relative brightness; a failed read is never guessed.</summary>
    public void AdjustBrightness(int delta)
    {
        if (_computer is null)
            return;
        ApplyToDisplays(display =>
        {
            var brightness = _brightnessController.GetBrightness(display);
            return brightness.HasValue ? Math.Clamp(brightness.Value + (long)delta, 0, 100) : (long?)null;
        }, "Brightness shortcut");
    }

    private void ApplyToDisplays(Func<Display, long?> getBrightness, string source)
    {
        var successes = 0;
        var failures = 0;
        try
        {
            foreach (var display in GetConnectedDisplays())
            {
                if (!_brightnessController.SupportsBrightness(display))
                    continue;
                var requested = getBrightness(display);
                if (!requested.HasValue)
                {
                    failures++;
                    continue;
                }
                var brightness = (int)Math.Clamp(requested.Value, 0, 100);
                if (_brightnessController.SetBrightness(display, brightness))
                {
                    _persistence.Remember(display, brightness);
                    successes++;
                }
                else
                    failures++;
            }
            Status = successes == 0 && failures == 0
                ? $"{source}: no connected monitor supports DDC/CI brightness."
                : $"{source}: applied to {successes} monitor(s)" +
                  (failures > 0 ? $"; {failures} failed. Open Adjust brightness to retry." : ".");
        }
        catch (Exception exception)
        {
            Status = $"{source} failed. Reconnect the monitor and retry in Adjust brightness.";
            _logger.Warn(exception, "Brightness automation failed.");
        }
        NotifyStatus();
    }

    private void OnTimerTick(object? sender, EventArgs args)
    {
        var now = DateTime.Now;
        var previous = _lastScheduleCheck;
        _lastScheduleCheck = now;
        if (_computer?.IsBrightnessScheduleEnabled != true)
            return;
        var schedule = FindLatestDueSchedule(_computer.BrightnessSchedules, previous, now);
        if (schedule is not null)
            ApplyToDisplays(_ => schedule.Brightness, $"Daily schedule at {schedule.Time}");
    }

    private void UpdateScheduleStatus()
    {
        Status = _computer?.IsBrightnessScheduleEnabled == true ? "Daily brightness schedule enabled; waiting for the next local time." :
            "Daily brightness schedule is off.";
        NotifyStatus();
    }

    /// <summary>After sleep, applies only the latest due occurrence; ignores invalid and future entries.</summary>
    public static BrightnessSchedule? FindLatestDueSchedule(IEnumerable<BrightnessSchedule>? schedules,
        DateTime previous, DateTime now)
    {
        if (schedules is null || now <= previous)
            return null;
        BrightnessSchedule? latest = null;
        var latestOccurrence = DateTime.MinValue;
        foreach (var schedule in schedules)
        {
            if (schedule is null || !schedule.IsValid || !schedule.TryGetTime(out var time))
                continue;
            var occurrence = now.Date + time;
            if (occurrence > now)
                occurrence = occurrence.AddDays(-1);
            if (occurrence <= previous || occurrence <= latestOccurrence)
                continue;
            latest = schedule;
            latestOccurrence = occurrence;
        }
        return latest;
    }

    private void NotifyStatus()
    {
        if (StatusChanged == null) return;
        foreach (Action subscriber in StatusChanged.GetInvocationList())
        {
            try { subscriber(); }
            catch (Exception exception) { _logger.Warn(exception, "Could not update brightness automation feedback."); }
        }
    }
}
