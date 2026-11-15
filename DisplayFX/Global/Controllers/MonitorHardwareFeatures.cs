using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using WindowsDisplayAPI;

namespace DisplayFX.Global.Controllers;

public sealed record VcpFeature(byte Code, string Name, uint? Current, uint? Maximum, IReadOnlyList<uint> AllowedValues)
{
    public string AllowedValuesText => AllowedValues.Count == 0 ? "Continuous value: 0…" + Maximum : string.Join(", ", AllowedValues.Select(value => $"{value} (0x{value:X2})"));
}

public partial class MonitorBrightnessController
{
    private static readonly IReadOnlyDictionary<byte, string> FeatureNames = new Dictionary<byte, string>
    {
        [0x10] = "Backlight brightness", [0x12] = "Physical contrast", [0x16] = "Red gain", [0x18] = "Green gain", [0x1A] = "Blue gain",
        [0x60] = "Input source", [0xD6] = "Power mode"
    };
    public virtual IReadOnlyList<VcpFeature> ReadVcpFeatures(Display display)
    {
        var channel = _channels.GetOrAdd(display.DevicePath, _ => new Channel());
        lock (channel)
        {
            if (IsDisplaySleeping) return Array.Empty<VcpFeature>();
            var handle = GetMonitorHandle(display);
            if (handle == IntPtr.Zero) return Array.Empty<VcpFeature>();
            var monitors = GetPhysicalMonitors(handle);
            if (monitors == null) return Array.Empty<VcpFeature>();
            try
            {
                if (!HasUnambiguousTarget(display, monitors) || !GetCapabilitiesStringLength(monitors[0].Handle, out var length) || length is < 1 or > 65536)
                    return Array.Empty<VcpFeature>();
                var capabilities = new StringBuilder((int)length);
                if (!CapabilitiesRequestAndCapabilitiesReply(monitors[0].Handle, capabilities, length)) return Array.Empty<VcpFeature>();
                var advertised = ParseVcpCapabilities(capabilities.ToString());
                return FeatureNames.Where(pair => advertised.ContainsKey(pair.Key)).Select(pair =>
                {
                    var read = GetVCPFeatureAndVCPFeatureReply(monitors[0].Handle, pair.Key, out _, out var current, out var maximum);
                    return new VcpFeature(pair.Key, pair.Value, read ? current : null, read ? maximum : null, advertised[pair.Key]);
                }).ToList();
            }
            finally { ReleasePhysicalMonitors(monitors); }
        }
    }
    public virtual bool SetVcpFeature(Display display, byte code, uint value)
    {
        if (!FeatureNames.ContainsKey(code)) return false;
        var channel = _channels.GetOrAdd(display.DevicePath, _ => new Channel());
        lock (channel)
        {
            if (IsDisplaySleeping && !(code == 0xD6 && value == 1)) return false;
            var feature = ReadVcpFeatures(display).FirstOrDefault(item => item.Code == code);
            if (feature == null || feature.AllowedValues.Count > 0 && !feature.AllowedValues.Contains(value) ||
                feature.AllowedValues.Count == 0 && (!feature.Maximum.HasValue || value > feature.Maximum.Value)) return false;
            var handle = GetMonitorHandle(display);
            if (handle == IntPtr.Zero) return false;
            var monitors = GetPhysicalMonitors(handle);
            if (monitors == null) return false;
            try
            {
                var accepted = !IsDisplaySleeping && HasUnambiguousTarget(display, monitors) && WriteVcpFeatureRaw(monitors[0].Handle, code, value);
                if (accepted && code == 0x10 && feature.Maximum is > 0)
                {
                    _versions.AddOrUpdate(display.DevicePath, 1, (_, version) => version + 1);
                    RecordStatus(display, new("", false, (int)Math.Clamp(Math.Round(value * 100d / feature.Maximum.Value), 0, 100)));
                }
                return accepted;
            }
            finally { ReleasePhysicalMonitors(monitors); }
        }
    }
    protected virtual bool WriteVcpFeatureRaw(IntPtr monitor, byte code, uint value) => SetVCPFeature(monitor, code, value);
    public static IReadOnlyDictionary<byte, IReadOnlyList<uint>> ParseVcpCapabilities(string capabilities)
    {
        var result = new Dictionary<byte, IReadOnlyList<uint>>();
        var start = capabilities.IndexOf("vcp(", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return result;
        var index = start + 4;
        while (index < capabilities.Length && capabilities[index] != ')')
        {
            if (char.IsWhiteSpace(capabilities[index])) { index++; continue; }
            var first = index;
            while (index < capabilities.Length && Uri.IsHexDigit(capabilities[index])) index++;
            if (first == index) { index++; continue; }
            if (!byte.TryParse(capabilities.AsSpan(first, index - first), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) continue;
            while (index < capabilities.Length && char.IsWhiteSpace(capabilities[index])) index++;
            var values = new List<uint>();
            if (index < capabilities.Length && capabilities[index] == '(')
            {
                var depth = 1; index++;
                while (index < capabilities.Length && depth > 0)
                {
                    if (capabilities[index] == '(') { depth++; index++; continue; }
                    if (capabilities[index] == ')') { depth--; index++; continue; }
                    first = index;
                    while (index < capabilities.Length && Uri.IsHexDigit(capabilities[index])) index++;
                    if (first == index) { index++; continue; }
                    if (depth == 1 && uint.TryParse(capabilities.AsSpan(first, index - first), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) values.Add(value);
                }
            }
            result[code] = values;
        }
        return result;
    }
    [DllImport("dxva2.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCapabilitiesStringLength(IntPtr monitor, out uint length);
    [DllImport("dxva2.dll", CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr monitor, StringBuilder capabilities, uint length);
}
