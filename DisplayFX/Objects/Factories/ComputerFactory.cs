using System;
using System.Collections.Generic;
using FluentResults;
using NLog;
using DisplayFX.Global.Controllers;
using DisplayFX.Objects.Entities;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;

namespace DisplayFX.Objects.Factories;

public class ComputerFactory
{
    private readonly MonitorFactory _monitorFactory;
    private readonly DisplayCache _displayCache;
    private readonly ILogger _logger;

    public ComputerFactory(MonitorFactory monitorFactory, DisplayCache displayCache, ILogger logger)
    {
        _monitorFactory = monitorFactory;
        _displayCache = displayCache;
        _logger = logger;
    }

    public Result<Computer> Create()
    {
        return DetectConnectedMonitors(false);
    }

    public Result<Computer> RefreshConnectedMonitors(Computer saved)
    {
        try
        {
            _displayCache.Refresh();
            var detected = DetectConnectedMonitors(true);
            if (detected.IsSuccess)
                MergeConnectedMonitors(saved, detected.Value);
            return detected;
        }
        catch (Exception e)
        {
            _logger.Warn(e, "Failed to refresh connected displays.");
            return Result.Fail<Computer>(new Error("Failed to refresh connected displays.").CausedBy(e));
        }
    }

    private Result<Computer> DetectConnectedMonitors(bool allowNoDisplays)
    {
        var computer = new Computer();
        try
        {
            var names = GetFriendlyNames();
            foreach (var display in _displayCache.GetDisplays())
            {
                try
                {
                    var screen = display.DisplayScreen;
                    if (screen == null)
                        continue;

                    var setting = screen.CurrentSetting;
                    if (setting == null)
                        continue;
                    var name = names.TryGetValue(display.DevicePath, out var friendlyName)
                        ? friendlyName
                        : display.DeviceName;
                    if (string.IsNullOrWhiteSpace(name))
                        name = screen.ScreenName;

                    computer.Monitors.Add(_monitorFactory.CreateDefault(
                        display.DevicePath, name, setting.Resolution, setting.Frequency));
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "A display became unavailable during detection; its saved profiles will be retained.");
                }
            }

            return allowNoDisplays || computer.Monitors.Count > 0
                ? Result.Ok(computer)
                : Result.Fail<Computer>("No connected displays were found.");
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to create monitor configuration.");
            return Result.Fail<Computer>(new Error("Failed to read connected displays.").CausedBy(e));
        }
    }

    internal static bool MergeConnectedMonitors(Computer saved, Computer detected)
    {
        var knownPaths = new Dictionary<string, Monitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in saved.Monitors)
            knownPaths.TryAdd(monitor.DisplayDevicePath, monitor);

        var changed = false;
        var detectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in detected.Monitors)
        {
            if (!detectedPaths.Add(monitor.DisplayDevicePath))
                continue;
            if (knownPaths.TryGetValue(monitor.DisplayDevicePath, out var existing))
            {
                changed |= existing.RefreshMetadata(monitor);
                continue;
            }

            saved.Monitors.Add(monitor);
            knownPaths.Add(monitor.DisplayDevicePath, monitor);
            changed = true;
        }

        return changed;
    }

    private Dictionary<string, string> GetFriendlyNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var target in PathDisplayTarget.GetDisplayTargets())
            {
                if (!target.IsAvailable)
                    continue;

                try
                {
                    var path = target.DevicePath;
                    var name = target.FriendlyName;
                    if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(name))
                        names[path] = name;
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Monitor name is unavailable; using the display device name.");
                }
            }
        }
        catch (Exception e)
        {
            _logger.Warn(e, "Monitor names are unavailable; using display device names.");
        }

        return names;
    }
}
