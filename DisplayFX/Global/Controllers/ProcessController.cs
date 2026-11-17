using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DisplayFX.Global.Controllers;

public sealed class ProcessController : IDisposable
{
    private IntPtr _foregroundHook;
    private WinEventCallback? _foregroundCallback;
    public bool StartForegroundWatch(Action changed)
    {
        if (_foregroundHook != IntPtr.Zero) return true;
        _foregroundCallback = (_, _, _, _, _, _, _) => changed();
        _foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, _foregroundCallback, 0, 0, 0x0002);
        return _foregroundHook != IntPtr.Zero;
    }
    public void Dispose()
    {
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
        _foregroundHook = IntPtr.Zero; _foregroundCallback = null;
    }
    private delegate void WinEventCallback(IntPtr hook, uint eventId, IntPtr window, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint minimum, uint maximum, IntPtr module, WinEventCallback callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(IntPtr hook);
    public string? GetForegroundExecutablePath()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        GetWindowThreadProcessId(window, out var processId);
        var process = OpenProcess(0x1000, false, processId);
        if (process == IntPtr.Zero) return null;
        try
        {
            var path = new StringBuilder(32768);
            var size = path.Capacity;
            return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString() : null;
        }
        finally { CloseHandle(process); }
    }

    public static bool MatchesExecutable(string? linkedPath, string? foregroundPath)
    {
        if (string.IsNullOrWhiteSpace(linkedPath) || string.IsNullOrWhiteSpace(foregroundPath)) return false;
        try
        {
            var linked = linkedPath.Trim().Trim('"');
            var foreground = foregroundPath.Trim().Trim('"');
            if (linked.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || foreground.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            return string.Equals(Path.IsPathRooted(linked) ? Path.GetFullPath(linked) : Path.GetFileName(linked),
                Path.IsPathRooted(linked) ? Path.GetFullPath(foreground) : Path.GetFileName(foreground), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
