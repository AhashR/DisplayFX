using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DisplayFX.Global.Controllers;

public sealed class ProcessController
{
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
