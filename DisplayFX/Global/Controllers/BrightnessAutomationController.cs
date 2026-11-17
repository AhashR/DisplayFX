using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using DisplayFX.Objects.Entities;
using NHotkey.Wpf;
using NLog;
using WindowsDisplayAPI;

namespace DisplayFX.Global.Controllers;

/// <summary>Opt-in global brightness shortcuts.</summary>
public class BrightnessAutomationController : IDisposable
{
    private const string IncreaseHotkeyId = "DisplayFX.Brightness.Increase";
    private const string DecreaseHotkeyId = "DisplayFX.Brightness.Decrease";
    private readonly DisplayCache _displayCache;
    private readonly MonitorBrightnessController _brightnessController;
    private readonly BrightnessPersistenceController _persistence;
    private readonly ILogger _logger;
    private Computer? _computer;
    private bool _started;
    private bool _hotkeysSuspended;
    private readonly Dispatcher _dispatcher;
    private readonly LinkedList<AutomationRequest> _pendingRequests = new();
    private Task _worker = Task.CompletedTask;
    private bool _processingRequests;
    private int _operationVersion;
    private sealed record AutomationRequest(long Delta);

    public BrightnessAutomationController(DisplayCache displayCache, MonitorBrightnessController brightnessController,
        BrightnessPersistenceController persistence, ILogger logger)
    {
        _displayCache = displayCache;
        _brightnessController = brightnessController;
        _persistence = persistence;
        _logger = logger;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public event Action? StatusChanged;
    public string Status { get; private set; } = "Brightness automation is off.";
    public string ShortcutStatus { get; private set; } = "Brightness shortcuts are off.";
    public string? ShortcutError { get; private set; }
    protected virtual DateTime LocalNow => DateTime.Now;

    public void Attach(Computer computer)
    {
        Stop();
        _computer = computer;
        ShortcutError = null;
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
        RefreshHotkeys();
    }

    public void Stop()
    {
        _started = false;
        Interlocked.Increment(ref _operationVersion);
        _pendingRequests.Clear();
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
        Interlocked.Increment(ref _operationVersion);
        _pendingRequests.Clear();
        ShortcutError = null;
        RefreshHotkeys();
    }

    public void RefreshHotkeys()
    {
        RemoveHotkeys();
        if (_computer is null || !_computer.IsBrightnessShortcutsEnabled)
        {
            ShortcutError = null;
            ShortcutStatus = "Brightness shortcuts are off.";
            NotifyStatus();
            return;
        }
        if (!_started || _hotkeysSuspended)
        {
            ShortcutStatus = ShortcutError ?? (_hotkeysSuspended ? "Shortcuts paused while settings are open." : "Shortcuts will activate when the app is ready.");
            NotifyStatus();
            return;
        }

        var increaseError = GetShortcutValidationError(_computer, true);
        var decreaseError = GetShortcutValidationError(_computer, false);
        if (increaseError is not null || decreaseError is not null)
        {
            ShortcutStatus = ShortcutError = increaseError ?? decreaseError!;
            NotifyStatus();
            return;
        }

        try
        {
            RegisterHotkey(IncreaseHotkeyId, _computer.BrightnessIncreaseKey!.Value,
                _computer.BrightnessIncreaseModifiers ?? ModifierKeys.None, 1);
            RegisterHotkey(DecreaseHotkeyId, _computer.BrightnessDecreaseKey!.Value,
                _computer.BrightnessDecreaseModifiers ?? ModifierKeys.None, -1);
            ShortcutError = null;
            ShortcutStatus = "Brightness shortcuts are active for all connected DDC/CI monitors.";
        }
        catch (Exception exception)
        {
            RemoveHotkeys();
            ShortcutStatus = ShortcutError = "Brightness shortcut is unavailable or used by another app. Choose another combination.";
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

    protected virtual void RegisterHotkey(string id, Key key, ModifierKeys modifiers, int direction)
    {
        HotkeyManager.Current.AddOrReplace(id, new KeyGesture(key, modifiers), (_, args) =>
        {
            if (_computer?.IsBrightnessShortcutsEnabled == true)
                AdjustBrightness(direction * Math.Clamp(_computer.BrightnessShortcutStep, 1, 100));
            args.Handled = true;
        });
    }

    protected virtual void RemoveHotkeys()
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
    public void AdjustBrightness(int delta) => _ = AdjustBrightnessAsync(delta);

    public Task AdjustBrightnessAsync(int delta)
    {
        if (!_dispatcher.CheckAccess())
            return _dispatcher.InvokeAsync(() => AdjustBrightnessAsync(delta)).Task.Unwrap();
        if (_computer is null || delta == 0)
            return Task.CompletedTask;
        return Enqueue(new AutomationRequest(delta));
    }

    private Task Enqueue(AutomationRequest request)
    {
        // Combine repeated steps in one direction while preserving reversal/clamping order.
        if (_pendingRequests.Last is { } last && Math.Sign(last.Value.Delta) == Math.Sign(request.Delta))
            last.Value = new AutomationRequest(Math.Clamp(last.Value.Delta + request.Delta, -100, 100));
        else
            _pendingRequests.AddLast(request);
        if (!_processingRequests)
        {
            _processingRequests = true;
            _worker = _dispatcher.InvokeAsync(ProcessRequestsAsync).Task.Unwrap();
        }
        return _worker;
    }

    private async Task ProcessRequestsAsync()
    {
        try
        {
            while (_pendingRequests.First is { } first)
            {
                _pendingRequests.RemoveFirst();
                var computer = _computer;
                if (computer != null)
                    await ApplyToDisplaysAsync(first.Value, computer, _operationVersion);
            }
        }
        finally { _processingRequests = false; }
    }

    private async Task ApplyToDisplaysAsync(AutomationRequest request, Computer computer, int version)
    {
        try
        {
            var result = await Task.Run(() =>
            {
                var applied = new List<(Display Display, int Brightness)>();
                var failures = 0;
                foreach (var display in GetConnectedDisplays())
                {
                    if (version != Volatile.Read(ref _operationVersion) || _brightnessController.IsDisplaySleeping) break;
                    if (!_brightnessController.SupportsBrightness(display)) continue;
                    if (version != Volatile.Read(ref _operationVersion) || _brightnessController.IsDisplaySleeping) break;
                    var brightness = _brightnessController.AdjustBrightness(display, request.Delta);
                    if (brightness.HasValue) applied.Add((display, brightness.Value));
                    else if (_brightnessController.IsDisplaySleeping) break;
                    else failures++;
                }
                return (Applied: applied, Failures: failures);
            });
            if (version != _operationVersion || !ReferenceEquals(computer, _computer)) return;
            // Continuations run on the dispatcher; only the UI thread updates the live settings model.
            foreach (var applied in result.Applied)
            {
                var latest = _brightnessController.GetLastKnownBrightness(applied.Display);
                if (!latest.HasValue || latest.Value == applied.Brightness)
                    _persistence.Remember(applied.Display, applied.Brightness);
            }
            Status = result.Applied.Count == 0 && result.Failures == 0
                ? $"Brightness shortcut: no connected monitor supports DDC/CI brightness."
                : $"Brightness shortcut: applied to {result.Applied.Count} monitor(s)" +
                  (result.Failures > 0 ? $"; {result.Failures} failed. Open Adjust brightness to retry." : ".");
        }
        catch (Exception exception)
        {
            if (version == _operationVersion && ReferenceEquals(computer, _computer))
                Status = $"Brightness shortcut failed. Reconnect the monitor and retry in Adjust brightness.";
            _logger.Warn(exception, "Brightness automation failed.");
        }
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
            if (schedule is null || !schedule.IsValid)
                continue;
            for (var day = 0; day <= 1; day++)
            {
                var occurrence = schedule.Occurrence(now.Date.AddDays(-day));
                if (!occurrence.HasValue || occurrence > now || occurrence <= previous || occurrence <= latestOccurrence) continue;
                latest = schedule; latestOccurrence = occurrence.Value;
            }
        }
        return latest;
    }
    public static bool HasScheduleConflicts(IReadOnlyList<BrightnessSchedule> schedules) =>
        schedules.GroupBy(schedule => schedule.Time, StringComparer.Ordinal).Any(group => group.Count() > 1);

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
