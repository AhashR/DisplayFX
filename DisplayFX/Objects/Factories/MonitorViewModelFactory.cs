using System;
using System.Collections.Generic;
using System.Linq;
using FluentResults;
using NLog;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.Monitors;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.Factories.Interfaces;
using WindowsDisplayAPI;

namespace DisplayFX.Objects.Factories;

public class MonitorViewModelFactory
{
    private readonly ILogger _logger;
    private readonly IProfileViewModelFactory _profileViewModelFactory;
    private readonly DisplayCache _displayCache;

    public MonitorViewModelFactory(IProfileViewModelFactory profileViewModelFactory, ILogger logger, DisplayCache displayCache)
    {
        _profileViewModelFactory = profileViewModelFactory;
        _logger = logger;
        _displayCache = displayCache;
    }

    public Result<MonitorViewModel> Create(Monitor monitor)
    {
        try
        {
            var display = _displayCache.GetDisplays().SingleOrDefault(d => d.DevicePath == monitor.DisplayDevicePath);
            if (display is null)
                return Result.Fail("Can't find display.");

            var monitorViewModel = new MonitorViewModel(monitor, display);

            foreach (var profile in monitor.Profiles)
            {
                var profileViewModel = _profileViewModelFactory.Create(profile, monitorViewModel);
                monitorViewModel.Profiles.Add(profileViewModel);
            }

            return monitorViewModel;
        }
        catch (Exception e)
        {
            _logger.Error(e);
            return Result.Fail("Can't find display.");
        }
    }
}