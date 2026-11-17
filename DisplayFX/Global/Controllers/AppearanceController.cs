using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace DisplayFX.Global.Controllers;

public static class AppearanceController
{
    private static bool _watching;
    public static void Apply()
    {
        var app = Application.Current;
        if (app == null) return;
        if (!_watching)
        {
            _watching = true;
            SystemParameters.StaticPropertyChanged += OnSystemChanged;
            app.Exit += (_, _) => { SystemParameters.StaticPropertyChanged -= OnSystemChanged; _watching = false; };
        }
        var names = new[] { "WindowBackgroundBrush", "CardBackgroundBrush", "CardHeaderBackgroundBrush", "InputBackgroundBrush", "BorderColorBrush", "TextPrimaryBrush", "TextMutedBrush", "HoverBrush", "SelectedBrush", "SidebarBackgroundBrush" };
        var colors = new[] { "#202020", "#292929", "#303030", "#303030", "#424242", "#F5F5F5", "#C4C4C4", "#353535", "#343434", "#1C1C1C" };
        for (var index = 0; index < names.Length; index++)
            app.Resources[names[index]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[index]));
        var accent = new SolidColorBrush(Color.FromRgb(96, 205, 255));
        app.Resources["SelectedForegroundBrush"] = app.Resources["TextPrimaryBrush"];
        app.Resources["PrimaryActionBrush"] = accent;
        app.Resources["PrimaryActionTextBrush"] = new SolidColorBrush(Color.FromRgb(16, 35, 45));
        foreach (var name in new[] { "AccentBlueBrush", "AccentBlueHoverBrush", "AccentColorBrush", "MahApps.Brushes.Accent" }) app.Resources[name] = accent;
        if (SystemParameters.HighContrast)
        {
            foreach (var name in new[] { "WindowBackgroundBrush", "CardBackgroundBrush", "CardHeaderBackgroundBrush", "InputBackgroundBrush", "SidebarBackgroundBrush" }) app.Resources[name] = SystemColors.WindowBrush;
            foreach (var name in new[] { "TextPrimaryBrush", "TextMutedBrush", "BorderColorBrush" }) app.Resources[name] = SystemColors.WindowTextBrush;
            foreach (var name in new[] { "AccentBlueBrush", "AccentColorBrush", "SelectedBrush" }) app.Resources[name] = SystemColors.HighlightBrush;
            app.Resources["HoverBrush"] = SystemColors.ControlBrush;
            app.Resources["SelectedForegroundBrush"] = SystemColors.HighlightTextBrush;
            app.Resources["PrimaryActionBrush"] = SystemColors.HighlightBrush;
            app.Resources["PrimaryActionTextBrush"] = SystemColors.HighlightTextBrush;
        }
        app.Resources["ContentFontSize"] = Math.Max(13d, SystemFonts.MessageFontSize);
    }
    private static void OnSystemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (Application.Current?.Dispatcher is { HasShutdownStarted: false } dispatcher)
            dispatcher.BeginInvoke(new Action(Apply));
    }
}
