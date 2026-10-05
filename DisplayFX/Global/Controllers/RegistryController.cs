using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DisplayFX.Global.Controllers;

public class RegistryController
{
    private static string AppName => "DisplayFX";

    public void RegisterForStartWithWindows(bool isStartWithWindows)
    {
        using var registryKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
        if (registryKey is null)
            throw new Exception("Unable to open registry startup key.");

        if (isStartWithWindows)
        {
            registryKey.SetValue(AppName, $"\"{Application.ExecutablePath}\"");
        }
        else
        {
            registryKey.DeleteValue(AppName, false);
        }
    }
}
