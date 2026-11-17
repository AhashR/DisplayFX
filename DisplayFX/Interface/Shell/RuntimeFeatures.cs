using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using DisplayFX.Global;
using DisplayFX.Global.Controllers;

namespace DisplayFX.Interface.Shell;
public partial class ShellViewModel
{
    private DateTimeOffset _nextDriverRecovery;
    private int _driverRecoveryAttempts;
    private LocalCommandServer? _commandServer;
    private void StartFeatureRuntime()
    {
        _commandServer = new LocalCommandServer(command => Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (command.Action == "list") return new LocalResponse(true, "Connected saved profiles", Monitors.SelectMany(monitor => monitor.Profiles.Select(profile => new LocalProfile(profile.Profile.Id, monitor.Name, profile.Name, profile.IsActive))).ToArray());
            if (_settingsOpen) return new LocalResponse(false, "Close Settings before applying profiles through the local interface.");
            var target = Monitors.SelectMany(monitor => monitor.Profiles).FirstOrDefault(profile => profile.Profile.Id == command.ProfileId);
            if (target == null) return new LocalResponse(false, "The requested profile is not connected.");
            var applied = await ApplyProfile(target);
            return new LocalResponse(applied, applied ? "Profile applied." : OperationStatus);
        }).Task.Unwrap());
    }
    private void RecoverDriverIfNeeded()
    {
        if (_settingsOpen || _profileWork > 0 || _nvidiaDisplays?.Count > 0 || _driverRecoveryAttempts >= 5 || DateTimeOffset.UtcNow < _nextDriverRecovery) return;
        var wasUnavailable = _driverRecoveryAttempts > 0;
        LoadNvidiaDisplays();
        if (_nvidiaDisplays?.Count > 0)
        {
            SelectMonitor(SelectedMonitor);
            _appSwitching.InvalidateHardware(Monitors.Select(monitor => monitor.Monitor));
            if (wasUnavailable) OperationStatus = "NVIDIA colour connection restored. Intended profiles will be reapplied.";
        }
    }
}
