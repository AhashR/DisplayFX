using System;
using System.Linq;
using System.Runtime.InteropServices;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;
using WindowsDisplayAPI.Native.Structures;

namespace DisplayFX.Global.Controllers;

public static class DisplayCapabilitiesController
{
    public static bool? IsAdvancedColorEnabled(Display display)
    {
        if (!display.IsAvailable) return null;
        try
        {
            var target = PathDisplayTarget.GetDisplayTargets().FirstOrDefault(item => string.Equals(item.DevicePath, display.DevicePath, StringComparison.OrdinalIgnoreCase));
            if (target == null) return null;
            var request = new AdvancedColorInfo { Type = 9, Size = (uint)Marshal.SizeOf<AdvancedColorInfo>(), Adapter = target.Adapter.AdapterId, TargetId = target.TargetId };
            return DisplayConfigGetDeviceInfo(ref request) == 0 ? (request.Flags & 2) != 0 : null;
        }
        catch { return null; }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorInfo { public uint Type, Size; public LUID Adapter; public uint TargetId, Flags, Encoding, BitsPerChannel; }
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo request);
}
