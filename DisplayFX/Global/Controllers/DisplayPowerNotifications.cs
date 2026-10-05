using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DisplayFX.Global.Controllers;

/// <summary>Receives display sleep/wake notifications through the app's long-lived tray window.</summary>
public sealed class DisplayPowerNotifications : IDisposable
{
    // Interactive applications use GUID_SESSION_DISPLAY_STATUS. See Microsoft Power Setting GUIDs.
    public static readonly Guid DisplayState = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
    private readonly HwndSource _source;
    private readonly Action<bool> _changed;
    private IntPtr _registration;
    public DisplayPowerNotifications(IntPtr window, Action<bool> changed)
    {
        _changed = changed;
        _source = HwndSource.FromHwnd(window);
        _source.AddHook(OnMessage);
        var state = DisplayState;
        _registration = RegisterPowerSettingNotification(window, ref state, 0);
    }
    public static bool? ParseState(Guid setting, uint dataLength, int value) =>
        setting == DisplayState && dataLength == sizeof(int) && value is >= 0 and <= 2 ? value != 0 : null;
    private IntPtr OnMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0218) return IntPtr.Zero;
        if (wParam.ToInt32() == 0x8013 && lParam != IntPtr.Zero)
        {
            var setting = Marshal.PtrToStructure<PowerSetting>(lParam);
            if (setting.Setting != DisplayState || setting.Length != sizeof(int)) return IntPtr.Zero;
            var awake = ParseState(setting.Setting, setting.Length, Marshal.ReadInt32(lParam, 20));
            if (awake.HasValue) _changed(awake.Value);
        }
        else if (wParam.ToInt32() == 4) _changed(false); // suspend
        else if (wParam.ToInt32() is 7 or 18) _changed(true); // resume or automatic resume
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (!_source.IsDisposed) _source.RemoveHook(OnMessage);
        if (_registration != IntPtr.Zero) UnregisterPowerSettingNotification(_registration);
        _registration = IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PowerSetting { public Guid Setting; public uint Length; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterPowerSettingNotification(IntPtr registration);
}
