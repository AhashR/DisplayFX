using System.Collections.Generic;
using System.Linq;
using FluentResults;
using DisplayFX.Global.Controllers;
using DisplayFX.Objects.Entities;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;

namespace DisplayFX.Objects.Factories;

public class ComputerFactory
{
    private readonly MonitorFactory _monitorFactory;
    private readonly DisplayCache _displayCache;
    private readonly PathDisplayTarget[] _pathDisplayTargets;

    public ComputerFactory(MonitorFactory monitorFactory, DisplayCache displayCache)
    {
        _monitorFactory = monitorFactory;
        _displayCache = displayCache;

        _pathDisplayTargets = PathDisplayTarget.GetDisplayTargets();
    }

    public Result<Computer> Create()
    {
        var computer = new Computer();
        var monitors = new List<Monitor>();

        foreach (var display in _displayCache.GetDisplays())
        {
            var resolution = display.DisplayScreen.CurrentSetting.Resolution;
            var frequency = display.DisplayScreen.CurrentSetting.Frequency;
            var displaySource = _pathDisplayTargets.Single(pds => pds.DevicePath == display.DevicePath);

            var monitor = _monitorFactory
                .CreateDefault(display.DevicePath, displaySource.FriendlyName, resolution, frequency);

            monitors.Add(monitor);
        }

        computer.Monitors.AddRange(monitors);

        return computer;
    }
}