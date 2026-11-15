using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using DisplayFX.Global;
using DisplayFX.Global.Controllers;
using Caliburn.Micro;
using Newtonsoft.Json;

namespace DisplayFX.Interface.Shell;
public partial class ShellViewModel
{
    private MonitorBrightnessController? _featureHardware;
    private DateTimeOffset _nextDriverRecovery;
    private int _driverRecoveryAttempts;
    private DateTimeOffset _nextColorObservation;
    private bool _observingColors;
    private LocalCommandServer? _commandServer;
    private void StartFeatureRuntime()
    {
        _featureHardware = IoC.Get<MonitorBrightnessController>();
        _featureHardware.DisplayPowerChanged += OnFeaturePowerChanged;
        RecordRuntimeVersion();
        _commandServer = new LocalCommandServer(command => Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (command.Action == "list") return new LocalResponse(true, "Connected saved profiles", Monitors.SelectMany(monitor => monitor.Profiles.Select(profile => new LocalProfile(profile.Profile.Id, monitor.Name, profile.Name, profile.IsActive))).ToArray());
            if (_settingsOpen) return new LocalResponse(false, "Close Settings or Workspace before applying profiles through the local interface.");
            var target = Monitors.SelectMany(monitor => monitor.Profiles).FirstOrDefault(profile => profile.Profile.Id == command.ProfileId);
            if (target == null) return new LocalResponse(false, "The requested profile is not connected.");
            var applied = await ApplyProfile(target);
            return new LocalResponse(applied, applied ? "Profile applied." : OperationStatus);
        }).Task.Unwrap());
        if (!Computer.Features.OnboardingCompleted && !Computer.IsStartMinimized)
            Application.Current.Dispatcher.BeginInvoke(new System.Action(OpenWelcomeGuide));
    }
    private void OnFeaturePowerChanged(bool awake)
    {
        if (!awake || !Computer.Features.ReapplyAfterWake) return;
        Application.Current?.Dispatcher.BeginInvoke(new System.Action(() => _appSwitching.InvalidateHardware(Monitors.Select(monitor => monitor.Monitor))));
    }
    private void RecoverDriverIfNeeded()
    {
        if (_settingsOpen || HasPreview || _profileWork > 0 || _nvidiaDisplays?.Count > 0 || _driverRecoveryAttempts >= 5 || DateTimeOffset.UtcNow < _nextDriverRecovery) return;
        var wasUnavailable = _driverRecoveryAttempts > 0;
        LoadNvidiaDisplays();
        if (_nvidiaDisplays?.Count > 0)
        {
            SelectMonitor(SelectedMonitor);
            _appSwitching.InvalidateHardware(Monitors.Select(monitor => monitor.Monitor));
            if (wasUnavailable) OperationStatus = "NVIDIA color connection restored. Intended profiles will be reapplied.";
            RecordRuntimeVersion();
        }
    }
    private async Task ObserveExternalColors()
    {
        if (_observingColors || !Computer.Features.DetectExternalColors || HasPreview || _profileWork > 0 || DateTimeOffset.UtcNow < _nextColorObservation) return;
        if (!await _colorGate.WaitAsync(0)) return;
        _nextColorObservation = DateTimeOffset.UtcNow.AddSeconds(15); _observingColors = true;
        try
        {
            var monitors = Monitors.ToList();
            foreach (var monitor in monitors)
                if (await Task.Run(() => _displayController.HasExternalColorChange(monitor.Display)) == true)
                    OperationStatus = $"Colors changed outside DisplayFX on {monitor.Name}. Check Windows color management or other color tools. No corrective write was sent.";
        }
        finally { _observingColors = false; _colorGate.Release(); }
    }
    private void RecordRuntimeVersion()
    {
        try
        {
            string driver;
            try { driver = "NVIDIA " + NvAPIWrapper.NVIDIA.DriverVersion; } catch { driver = "NVIDIA API unavailable / standard Windows backend"; }
            var state = JsonConvert.SerializeObject(new { App = Global.AppVersion.Current, Driver = driver });
            var directory = Path.GetDirectoryName(_dataController.DataPath)!;
            Directory.CreateDirectory(directory);
            var statePath = Path.Combine(directory, "Runtime.json");
            if (!File.Exists(statePath) || File.ReadAllText(statePath) != state)
            {
                _logger.Info("Application/driver change: {0}", state);
                File.WriteAllText(statePath, state);
                StatusHistory.Add(new(DateTimeOffset.Now, Objects.Entities.OperationSeverity.Information, "Application/driver state: " + state));
            }
        }
        catch (Exception ex) { _logger.Warn(ex, "Could not record the local application/driver state."); }
    }
}
