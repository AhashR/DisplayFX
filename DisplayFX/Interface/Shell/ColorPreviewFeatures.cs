using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.Monitors;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Interface.Shell;

public partial class ShellViewModel
{
    private sealed record PreviewState(MonitorViewModel Monitor, NvAPIWrapper.Display.Display? Nvidia,
        DisplayController.ColorSnapshot Colors, ProfileSetting Settings, Guid ProfileId, int Version, DateTimeOffset Deadline, CancellationTokenSource Cancellation);
    private PreviewState? _previewState;
    private bool _previewStarting;
    private long _previewGeneration;
    public bool HasPreview => _previewState != null;
    public string PreviewLabel => _previewState == null ? "" : $"Preview: {Math.Max(0, (int)Math.Ceiling((_previewState.Deadline - DateTimeOffset.UtcNow).TotalSeconds))}s — keep or automatically restore";
    public Task PreviewProfile() => BeginPreview(false);
    public Task CompareOriginal() => BeginPreview(true);
    private async Task BeginPreview(bool original)
    {
        if (!CanApply || SelectedMonitor == null || SelectedProfile == null) return;
        await CancelPreview();
        var generation = ++_previewGeneration;
        var version = _modelVersion;
        _previewStarting = true;
        NotifyOfPropertyChange(nameof(IsApplying)); NotifyOfPropertyChange(nameof(ApplyingText));
        var monitor = SelectedMonitor;
        var profile = SelectedProfile;
        var settings = profile.ProfileSettings!.ProfileSetting.Copy();
        await _colorGate.WaitAsync();
        try
        {
            var nvidia = _nvidiaDisplays?.FirstOrDefault(display => display.Name == monitor.ScreenName);
            var display = monitor.Display;
            var colors = await Task.Run(() => _displayController.CaptureColors(display, nvidia));
            if (generation != _previewGeneration || version != _modelVersion) return;
            var applied = await Task.Run(() => original ? _displayController.RestoreOriginalColors(display, nvidia) : _displayController.UpdateColorSettings(display, settings, nvidia));
            if (!applied) { OperationStatus = _displayController.LastError ?? "Preview failed."; return; }
            if (generation != _previewGeneration || version != _modelVersion)
            {
                if (version == _modelVersion) await Task.Run(() => _displayController.RestoreColors(display, nvidia, colors));
                return;
            }
            var state = new PreviewState(monitor, nvidia, colors, settings, profile.Profile.Id, _modelVersion, DateTimeOffset.UtcNow.AddSeconds(15), new());
            _previewState = state;
            NotifyPreview();
            _ = ExpirePreview(state, state.Cancellation.Token);
        }
        catch (Exception ex) { ReportFailure("Could not start a safe color preview.", ex); }
        finally { _previewStarting = false; _colorGate.Release(); NotifyOfPropertyChange(nameof(IsApplying)); NotifyOfPropertyChange(nameof(ApplyingText)); }
    }
    private async Task ExpirePreview(PreviewState state, CancellationToken token)
    {
        try
        {
            while (ReferenceEquals(_previewState, state) && DateTimeOffset.UtcNow < state.Deadline)
            { await Task.Delay(250, token); NotifyOfPropertyChange(nameof(PreviewLabel)); }
            if (ReferenceEquals(_previewState, state)) await CancelPreview();
        }
        catch (OperationCanceledException) { }
    }
    public async Task CancelPreview()
    {
        _previewGeneration++;
        var state = _previewState;
        if (state == null)
        {
            if (_previewStarting) { await _colorGate.WaitAsync(); _colorGate.Release(); }
            return;
        }
        _previewState = null;
        state.Cancellation.Cancel(); state.Cancellation.Dispose();
        NotifyPreview();
        await _colorGate.WaitAsync();
        try
        {
            if (state.Version != _modelVersion || !Monitors.Contains(state.Monitor))
            { OperationStatus = "Display connection changed during preview. Refresh and apply the intended profile."; return; }
            if (!await Task.Run(() => _displayController.RestoreColors(state.Monitor.Display, state.Nvidia, state.Colors)))
                OperationStatus = _displayController.LastError ?? "Preview colors could not be restored.";
        }
        finally { _colorGate.Release(); }
    }
    public async Task KeepPreview()
    {
        var state = _previewState;
        if (state == null) return;
        state.Cancellation.Cancel();
        await _colorGate.WaitAsync();
        var shouldCancel = false;
        try
        {
            if (!ReferenceEquals(_previewState, state)) return;
            if (state.Version != _modelVersion) { shouldCancel = true; return; }
            var profile = state.Monitor.Profiles.FirstOrDefault(item => item.Profile.Id == state.ProfileId);
            if (profile == null) { shouldCancel = true; return; }
            foreach (var other in state.Monitor.Profiles) other.IsActive = ReferenceEquals(other, profile);
            _appSwitching.ManualSelection(state.Monitor.Monitor); SelectProfile(profile);
            _previewState = null; state.Cancellation.Dispose(); NotifyPreview();
            Write(); UpdateTrayTooltip();
            OperationStatus = "Preview kept. Save the profile to store edited values.";
        }
        catch (Exception ex) { ReportFailure("Preview colors kept, but active profile state could not be saved.", ex); }
        finally { _colorGate.Release(); if (shouldCancel) await CancelPreview(); }
    }
    public async Task RestoreOriginal()
    {
        await CancelPreview();
        var monitor = SelectedMonitor;
        if (monitor == null) return;
        await _colorGate.WaitAsync();
        try
        {
            var nvidia = _nvidiaDisplays?.FirstOrDefault(display => display.Name == monitor.ScreenName);
            if (!await Task.Run(() => _displayController.RestoreOriginalColors(monitor.Display, nvidia)))
            { OperationStatus = _displayController.LastError ?? "Original colors are unavailable."; return; }
            foreach (var profile in monitor.Profiles) profile.Deactivate();
            _appSwitching.ManualSelection(monitor.Monitor);
            Write();
            OperationStatus = "Original session colors restored. No saved profile is currently active on this monitor.";
        }
        catch (Exception ex) { ReportFailure("Could not restore original colors.", ex); }
        finally { _colorGate.Release(); }
    }
    private void NotifyPreview()
    {
        NotifyOfPropertyChange(nameof(HasPreview)); NotifyOfPropertyChange(nameof(PreviewLabel));
    }
}
