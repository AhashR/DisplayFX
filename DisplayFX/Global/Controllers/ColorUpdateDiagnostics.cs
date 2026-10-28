using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using WindowsDisplayAPI;

namespace DisplayFX.Global.Controllers;

/// <summary>Opt-in, read-only observations. Never guesses or restores physical brightness.</summary>
public sealed class ColorUpdateDiagnostics
{
    private readonly DisplayCache _cache;
    private readonly MonitorBrightnessController _brightness;
    private readonly ILogger _logger;
    public bool Enabled { get; set; }
    public string? Warning { get; private set; }

    public ColorUpdateDiagnostics(DisplayCache cache, MonitorBrightnessController brightness, ILogger logger)
    { _cache = cache; _brightness = brightness; _logger = logger; }

    public async Task<bool> ApplyAsync(Func<bool> apply)
    {
        Warning = null;
        if (!Enabled) return await Task.Run(apply);
        var before = await Task.Run(() =>
        {
            var levels = new List<(Display Display, int Level)>();
            foreach (var display in _cache.GetDisplays())
                if (_brightness.GetBrightness(display) is int level) levels.Add((display, level));
            return levels;
        });
        var success = await Task.Run(apply);
        foreach (var delay in new[] { 0, 300, 1200 })
        {
            if (delay > 0) await Task.Delay(delay);
            await Task.Run(() =>
            {
                foreach (var (display, level) in before)
                    if (_brightness.GetBrightness(display) is int current && current != level)
                    {
                        Warning = "Physical brightness changed during color application. Check monitor dynamic-contrast/HDR settings or other brightness apps; see diagnostic logs.";
                        _logger.Warn("Brightness observation for {0}: before={1}, after={2}, delay={3}ms. No corrective write was sent.", display.DevicePath, level, current, delay);
                    }
            });
        }
        return success;
    }
}
