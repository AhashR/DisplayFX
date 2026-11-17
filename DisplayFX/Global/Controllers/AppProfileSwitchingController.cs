using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Global.Controllers;

/// <summary>Plans app switches while retaining each monitor's manual profile. Hardware application belongs to the shell.</summary>
public sealed class AppProfileSwitchingController
{
    private readonly Dictionary<Monitor, Profile> _baselines = new();
    private string? _lastApplication;
    private long _revision;
    private long _resetRevision;
    private readonly HashSet<Monitor> _invalidated = new();
    public void InvalidateHardware(IEnumerable<Monitor> monitors) { foreach (var monitor in monitors) _invalidated.Add(monitor); Refresh(); }
    public void Reset() { _baselines.Clear(); _invalidated.Clear(); _resetRevision++; Refresh(); }
    public void Refresh() { _lastApplication = null; _revision++; }
    public void ManualSelection(Monitor monitor)
    {
        _baselines.Remove(monitor);
        foreach (var profile in monitor.Profiles) profile.IsActiveBeforeAutomaticSwitch = null;
    }

    public void Update(string? application, IEnumerable<Monitor> monitors, Func<Profile, bool> apply)
        => UpdateAsync(application, monitors, profile => Task.FromResult(apply(profile))).GetAwaiter().GetResult();

    public async Task UpdateAsync(string? application, IEnumerable<Monitor> monitors, Func<Profile, Task<bool>> apply)
    {
        if (string.IsNullOrWhiteSpace(application) || string.Equals(application, _lastApplication, StringComparison.OrdinalIgnoreCase)) return;
        var revision = _revision;
        var resetRevision = _resetRevision;
        var allSucceeded = true;
        foreach (var monitor in monitors)
        {
            var matches = monitor.Profiles.Where(profile => MatchesProfile(profile, application)).Take(2).ToList();
            var linked = matches.Count == 1 ? matches[0] : null;
            if (linked != null)
            {
                if (linked.IsActive && !_invalidated.Contains(monitor)) continue;
                var previous = monitor.Profiles.FirstOrDefault(profile => profile.IsActive);
                var starting = !_baselines.ContainsKey(monitor) && previous != null;
                if (starting)
                    foreach (var profile in monitor.Profiles) profile.IsActiveBeforeAutomaticSwitch = ReferenceEquals(profile, previous);
                if (await apply(linked))
                {
                    if (resetRevision != _resetRevision) return;
                    if (revision == _revision) _invalidated.Remove(monitor);
                    if (previous != null) _baselines.TryAdd(monitor, previous);
                }
                else
                {
                    if (resetRevision != _resetRevision) return;
                    if (starting) foreach (var profile in monitor.Profiles) profile.IsActiveBeforeAutomaticSwitch = null;
                    allSucceeded = false;
                }
            }
            else if (_baselines.TryGetValue(monitor, out var baseline))
            {
                var target = monitor.Profiles.FirstOrDefault(profile => profile.Id == monitor.ReferenceProfileId) ??
                    (monitor.Profiles.Contains(baseline) ? baseline : monitor.Profiles.FirstOrDefault(profile => profile.IsDefault));
                var flags = monitor.Profiles.ToDictionary(profile => profile, profile => profile.IsActiveBeforeAutomaticSwitch);
                foreach (var profile in monitor.Profiles) profile.IsActiveBeforeAutomaticSwitch = null;
                if (target == null || target.IsActive && !_invalidated.Contains(monitor) || await apply(target))
                {
                    if (resetRevision != _resetRevision) return;
                    _baselines.Remove(monitor);
                    if (revision == _revision) _invalidated.Remove(monitor);
                }
                else
                {
                    if (resetRevision != _resetRevision) return;
                    foreach (var entry in flags) entry.Key.IsActiveBeforeAutomaticSwitch = entry.Value;
                    allSucceeded = false;
                }
            }
            else if (_invalidated.Contains(monitor))
            {
                var active = monitor.Profiles.FirstOrDefault(profile => profile.IsActive);
                if (active == null || await apply(active))
                {
                    if (resetRevision != _resetRevision) return;
                    if (revision == _revision) _invalidated.Remove(monitor);
                }
                else allSucceeded = false;
            }
        }
        // A failed hardware update stays retryable even if the foreground app has not changed.
        if (allSucceeded && revision == _revision) _lastApplication = application;
    }
    public static bool MatchesProfile(Profile profile, string? application) =>
        !profile.IsDefault && ProcessController.MatchesExecutable(profile.LinkedExecutablePath, application);
}
