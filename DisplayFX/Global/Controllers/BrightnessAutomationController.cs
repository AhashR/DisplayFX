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
    private readonly Dispatcher _dispatcher;
    private readonly LinkedList<AutomationRequest> _pendingRequests = new();
    private Task _worker = Task.CompletedTask;
    private bool _processingRequests;
    private int _operationVersion;
    private sealed record AutomationRequest(long? Delta, int? Brightness, string Source, DateTime? ScheduleSince = null,
        string? OnlyDevicePath = null, long? ExpectedVersion = null, int RetryAttempt = 0);
    private sealed record ScheduleRetry(AutomationRequest Request, long Version, DateTime Due);
    private readonly Dictionary<string, ScheduleRetry> _scheduleRetries = new(StringComparer.OrdinalIgnoreCase);

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
        _lastScheduleCheck = LocalNow;
        _brightnessController.DisplayPowerChanged += OnDisplayPowerChanged;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
        UpdateScheduleStatus();
        RefreshHotkeys();
    }

    public void Stop()
    {
        _started = false;
        _brightnessController.DisplayPowerChanged -= OnDisplayPowerChanged;
        Interlocked.Increment(ref _operationVersion);
        _pendingRequests.Clear();
        _scheduleRetries.Clear();
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
        _lastScheduleCheck = LocalNow;
        Interlocked.Increment(ref _operationVersion);
        _pendingRequests.Clear();
        _scheduleRetries.Clear();
        ShortcutError = null;
        UpdateScheduleStatus();
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
        return Enqueue(new AutomationRequest(delta, null, "Brightness shortcut"));
    }

    private Task Enqueue(AutomationRequest request)
    {
        // Combine repeated steps in one direction while preserving reversal/clamping order.
        if (request.Delta.HasValue && _pendingRequests.Last?.Value is { Delta: not null } last &&
            Math.Sign(last.Delta.Value) == Math.Sign(request.Delta.Value))
            _pendingRequests.Last.Value = last with { Delta = Math.Clamp(last.Delta.Value + request.Delta.Value, -100, 100) };
        else
        {
            if (request.ScheduleSince.HasValue)
            {
                // A slow monitor should receive only the newest queued daily level.
                for (var node = _pendingRequests.First; node != null;)
                {
                    var next = node.Next;
                    if (node.Value.ScheduleSince.HasValue && (request.OnlyDevicePath == null ||
                        string.Equals(request.OnlyDevicePath, node.Value.OnlyDevicePath, StringComparison.OrdinalIgnoreCase))) _pendingRequests.Remove(node);
                    node = next;
                }
            }
            _pendingRequests.AddLast(request);
        }
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
            var expectedPaths = computer.Monitors.Select(monitor => monitor.DisplayDevicePath).ToArray();
            var result = await Task.Run(() =>
            {
                var applied = new List<(Display Display, int Brightness)>();
                var failed = new List<(string Path, long Version)>();
                var failures = 0;
                var deferred = false;
                var displays = GetConnectedDisplays();
                foreach (var display in displays)
                {
                    if (request.OnlyDevicePath != null && !string.Equals(display.DevicePath, request.OnlyDevicePath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (request.ExpectedVersion.HasValue && request.ExpectedVersion != _brightnessController.GetVersion(display)) continue;
                    if (version != Volatile.Read(ref _operationVersion)) break;
                    if (_brightnessController.IsDisplaySleeping) { deferred = true; break; }
                    if (!_brightnessController.SupportsBrightness(display))
                    {
                        if (request.ScheduleSince.HasValue) { failures++; failed.Add((display.DevicePath, _brightnessController.GetVersion(display))); }
                        continue;
                    }
                    if (version != Volatile.Read(ref _operationVersion)) break;
                    if (_brightnessController.IsDisplaySleeping) { deferred = true; break; }
                    var brightness = request.Delta.HasValue ? _brightnessController.AdjustBrightness(display, request.Delta.Value) :
                        (request.ExpectedVersion.HasValue ? _brightnessController.SetBrightnessIfCurrent(display, request.Brightness!.Value, request.ExpectedVersion.Value) :
                            _brightnessController.SetBrightness(display, request.Brightness!.Value)) ? request.Brightness : null;
                    if (brightness.HasValue) applied.Add((display, brightness.Value));
                    else if (_brightnessController.IsDisplaySleeping) { deferred = true; break; }
                    else { failures++; failed.Add((display.DevicePath, _brightnessController.GetVersion(display))); }
                }
                if (request.ScheduleSince.HasValue)
                    foreach (var path in expectedPaths.Where(path => (request.OnlyDevicePath == null || string.Equals(path, request.OnlyDevicePath, StringComparison.OrdinalIgnoreCase)) &&
                        !displays.Any(display => string.Equals(display.DevicePath, path, StringComparison.OrdinalIgnoreCase))))
                        failed.Add((path, request.ExpectedVersion ?? 0));
                return (Applied: applied, Failures: failures, Deferred: deferred, Failed: failed);
            });
            if (version != _operationVersion || !ReferenceEquals(computer, _computer)) return;
            // Continuations run on the dispatcher; only the UI thread updates the live settings model.
            foreach (var applied in result.Applied)
            {
                var latest = _brightnessController.GetLastKnownBrightness(applied.Display);
                if (!latest.HasValue || latest.Value == applied.Brightness)
                    _persistence.Remember(applied.Display, applied.Brightness);
            }
            if (request.ScheduleSince.HasValue && !result.Deferred && request.RetryAttempt < 8)
                foreach (var failed in result.Failed)
                    _scheduleRetries[failed.Path] = new ScheduleRetry(request with { OnlyDevicePath = failed.Path,
                        ExpectedVersion = failed.Version, RetryAttempt = request.RetryAttempt + 1 }, failed.Version, LocalNow.AddSeconds(15));
            if (result.Deferred && request.ScheduleSince.HasValue)
            {
                if (request.ScheduleSince.Value < _lastScheduleCheck) _lastScheduleCheck = request.ScheduleSince.Value;
                if (!_brightnessController.IsDisplaySleeping) CheckSchedules(LocalNow);
                return;
            }
            Status = result.Applied.Count == 0 && result.Failures == 0
                ? $"{request.Source}: no connected monitor supports DDC/CI brightness."
                : $"{request.Source}: applied to {result.Applied.Count} monitor(s)" +
                  (result.Failures > 0 ? $"; {result.Failures} failed. Open Adjust brightness to retry." : ".");
        }
        catch (Exception exception)
        {
            if (version == _operationVersion && ReferenceEquals(computer, _computer))
                Status = $"{request.Source} failed. Reconnect the monitor and retry in Adjust brightness.";
            _logger.Warn(exception, "Brightness automation failed.");
        }
        NotifyStatus();
    }

    private void OnDisplayPowerChanged(bool awake)
    {
        if (!awake || !_started || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (_started) CheckSchedules(LocalNow);
        }));
    }

    private void OnTimerTick(object? sender, EventArgs args) => CheckSchedules(LocalNow);

    private void CheckSchedules(DateTime now)
    {
        if (_computer?.IsBrightnessScheduleEnabled != true)
        {
            _lastScheduleCheck = now;
            return;
        }
        // Keep the interval open while displays sleep, then apply its latest due entry on wake.
        if (_brightnessController.IsDisplaySleeping) return;
        var previous = _lastScheduleCheck;
        _lastScheduleCheck = now;
        var schedule = FindLatestDueSchedule(_computer.BrightnessSchedules, previous, now);
        if (schedule is not null)
        {
            _scheduleRetries.Clear();
            _ = Enqueue(new AutomationRequest(null, schedule.Brightness, $"Daily schedule at {schedule.Time}", previous));
        }
        else
            foreach (var retry in _scheduleRetries.Where(pair => pair.Value.Due <= now).ToList())
            {
                _scheduleRetries.Remove(retry.Key);
                _ = Enqueue(retry.Value.Request);
            }
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
