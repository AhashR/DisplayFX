using System;
using System.Windows;
using DisplayFX.Global;

namespace DisplayFX.Interface.Settings;

public partial class SettingsView : Window
{
    public SettingsView()
    {
        InitializeComponent();
        MaxWidth = Math.Max(240, SystemParameters.WorkArea.Width - 24);
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 32);
        SourceInitialized += (_, _) => NativeWindowTheme.Apply(this);
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel model) await model.CheckForUpdates();
    }
}
