using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DisplayFX.Global.Controllers;

public class RegistryController
{
    private static string AppName => "DisplayFX";
    private static string LegacyAppName => "DisplayFX";

    public void RegisterForStartWithWindows(bool isStartWithWindows)
    {
        var registryKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
        if (registryKey is null)
            throw new Exception("Unable to open registry startup key.");

        // Clean up legacy key if present
        try
        {
            if (registryKey.GetValue(LegacyAppName) != null)
                registryKey.DeleteValue(LegacyAppName, false);
        }
        catch { }

        if (isStartWithWindows)
        {
            registryKey.SetValue(AppName, Application.ExecutablePath);
        }
        else
        {
            try
            {
                if (registryKey.GetValue(AppName) != null)
                    registryKey.DeleteValue(AppName, false);
            }
            catch { }
        }
    }
}