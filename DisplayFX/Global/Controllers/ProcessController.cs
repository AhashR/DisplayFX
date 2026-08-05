using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DisplayFX.Global.Controllers;

public class ProcessController
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public string? GetForegroundExecutablePath(IntPtr foregroundWindow = default)
    {
        if (foregroundWindow == IntPtr.Zero)
            foregroundWindow = GetForegroundWindow();

        if (foregroundWindow == IntPtr.Zero)
            return null;

        GetWindowThreadProcessId(foregroundWindow, out var processId);
        if (processId == 0)
            return null;

        var processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero)
            return null;

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(processHandle, 0, buffer, ref size)
                ? buffer.ToString()
                : null;
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }
}