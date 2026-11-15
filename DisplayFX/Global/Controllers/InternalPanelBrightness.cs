using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using WindowsDisplayAPI;

namespace DisplayFX.Global.Controllers;

public interface IInternalPanelBrightness
{
    bool TryRead(Display display, out int brightness);
    bool Supports(Display display);
    bool Set(Display display, int brightness);
    void Invalidate();
}
public sealed class WmiInternalPanelBrightness : IInternalPanelBrightness
{
    private readonly object _sync = new();
    private readonly Dictionary<string, int> _levels = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _nextRead;
    public void Invalidate() { lock (_sync) _nextRead = DateTime.MinValue; }
    public bool Supports(Display display) => TryRead(display, out _);
    public bool TryRead(Display display, out int brightness)
    {
        lock (_sync)
        {
            if (DateTime.UtcNow >= _nextRead) ReadLevels();
            var names = _levels.Keys.Where(name => MatchesInstance(display.DevicePath, name)).ToList();
            if (names.Count == 1) { brightness = _levels[names[0]]; return true; }
            brightness = 0; return false;
        }
    }
    public static bool MatchesInstance(string devicePath, string instanceName)
    {
        var components = devicePath.Replace(@"\\?\", "").Split('#');
        if (components.Length < 3) return false;
        var key = string.Join("\\", components.Take(3));
        return string.Equals(key, instanceName, StringComparison.OrdinalIgnoreCase) || instanceName.StartsWith(key + "_", StringComparison.OrdinalIgnoreCase);
    }
    private void ReadLevels()
    {
        _levels.Clear();
        _nextRead = DateTime.UtcNow.AddSeconds(2);
        try
        {
            Query("WmiMonitorBrightness", item =>
            {
                string name = item.InstanceName;
                int level = Convert.ToInt32(item.CurrentBrightness);
                if (level is >= 0 and <= 100) _levels[name] = level;
            });
        }
        catch (Exception) { _levels.Clear(); _nextRead = DateTime.UtcNow.AddSeconds(15); }
    }
    public bool Set(Display display, int brightness)
    {
        lock (_sync)
        {
            if (!TryRead(display, out _)) return false;
            var selectedInstance = _levels.Keys.Single(name => MatchesInstance(display.DevicePath, name));
            var accepted = false;
            try
            {
                Query("WmiMonitorBrightnessMethods", item =>
                {
                    if (!string.Equals(selectedInstance, (string)item.InstanceName, StringComparison.OrdinalIgnoreCase)) return;
                    accepted = Convert.ToUInt32(item.WmiSetBrightness(1u, (byte)Math.Clamp(brightness, 0, 100))) == 0;
                });
                if (accepted) _nextRead = DateTime.MinValue;
            }
            catch (Exception) { return false; }
            return accepted;
        }
    }
    private static void Query(string className, Action<dynamic> visit)
    {
        dynamic? locator = null, service = null, results = null;
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator") ?? throw new InvalidOperationException("WMI is unavailable.");
            locator = Activator.CreateInstance(type)!;
            service = locator.ConnectServer(".", @"root\wmi");
            results = service.ExecQuery("SELECT * FROM " + className + " WHERE Active=TRUE");
            foreach (dynamic item in (IEnumerable)results)
                try { visit(item); } finally { Release(item); }
        }
        finally { Release(results); Release(service); Release(locator); }
    }
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
}
