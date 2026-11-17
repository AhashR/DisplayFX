using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DisplayFX.Global;

internal static class NativeWindowTheme
{
    internal static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        const int useImmersiveDarkMode = 20;
        var dark = SystemParameters.HighContrast ? 0 : 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, useImmersiveDarkMode, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
