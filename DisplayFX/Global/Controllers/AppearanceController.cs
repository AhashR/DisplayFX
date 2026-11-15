using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using DisplayFX.Objects.Entities;
using Microsoft.Win32;

namespace DisplayFX.Global.Controllers;

public static class AppearanceController
{
    private static FeatureSettings? _current;
    private static bool _watching;
    public static void Apply(FeatureSettings settings)
    {
        _current = settings;
        var app = Application.Current;
        if (app == null) return;
        if (!_watching)
        {
            _watching = true;
            SystemParameters.StaticPropertyChanged += OnSystemChanged;
            app.Exit += (_, _) => { SystemParameters.StaticPropertyChanged -= OnSystemChanged; _watching = false; };
        }
        bool dark = settings.Theme != "Light";
        if (settings.Theme == "System")
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = personalize?.GetValue("AppsUseLightTheme") is not int value || value == 0;
        }
        var names = new[] { "WindowBackgroundBrush", "CardBackgroundBrush", "CardHeaderBackgroundBrush", "InputBackgroundBrush", "BorderColorBrush", "TextPrimaryBrush", "TextMutedBrush", "HoverBrush", "SelectedBrush" };
        var colors = dark
            ? new[] { "#11151C", "#1A202A", "#212936", "#212936", "#475569", "#EAF0F8", "#B5C2D4", "#303D50", "#293B52" }
            : new[] { "#F5F7FA", "#FFFFFF", "#E9EEF5", "#EDF1F7", "#64748B", "#152035", "#475569", "#DCE5F0", "#D6E7FF" };
        for (var index = 0; index < names.Length; index++)
            app.Resources[names[index]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[index]));
        var accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.Accent));
        app.Resources["SelectedForegroundBrush"] = app.Resources["TextPrimaryBrush"];
        app.Resources["PrimaryActionBrush"] = new SolidColorBrush(Color.FromRgb(49, 87, 127));
        app.Resources["PrimaryActionTextBrush"] = Brushes.White;
        foreach (var name in new[] { "AccentBlueBrush", "AccentBlueHoverBrush", "AccentColorBrush", "MahApps.Brushes.Accent" }) app.Resources[name] = accent;
        if (SystemParameters.HighContrast)
        {
            foreach (var name in new[] { "WindowBackgroundBrush", "CardBackgroundBrush", "CardHeaderBackgroundBrush", "InputBackgroundBrush" }) app.Resources[name] = SystemColors.WindowBrush;
            foreach (var name in new[] { "TextPrimaryBrush", "TextMutedBrush", "BorderColorBrush" }) app.Resources[name] = SystemColors.WindowTextBrush;
            foreach (var name in new[] { "AccentBlueBrush", "AccentColorBrush", "SelectedBrush" }) app.Resources[name] = SystemColors.HighlightBrush;
            app.Resources["HoverBrush"] = SystemColors.ControlBrush;
            app.Resources["SelectedForegroundBrush"] = SystemColors.HighlightTextBrush;
            app.Resources["PrimaryActionBrush"] = SystemColors.HighlightBrush;
            app.Resources["PrimaryActionTextBrush"] = SystemColors.HighlightTextBrush;
        }
        app.Resources["ControlPadding"] = settings.Density == "Compact" ? new Thickness(8, 5, 8, 5) : new Thickness(12, 9, 12, 9);
        app.Resources["IconSize"] = settings.Density == "Compact" ? 32d : 40d;
        app.Resources["ContentFontSize"] = Math.Max(13d, SystemFonts.MessageFontSize) * settings.TextScalePercent / 100d * (settings.Density == "Compact" ? .95 : 1);
        LocalizationController.Instance.SetLanguage(settings.Language);
    }
    private static void OnSystemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_current != null && Application.Current?.Dispatcher is { HasShutdownStarted: false } dispatcher)
            dispatcher.BeginInvoke(new Action(() => { if (_current != null) Apply(_current); }));
    }
}
