using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Interface.Settings;

public partial class SettingsView : Window
{
    public SettingsView() => InitializeComponent();
    private void IncreaseShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel model) model.BeginRecording(true);
        ((Button)sender).Focus();
    }
    private void DecreaseShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel model) model.BeginRecording(false);
        ((Button)sender).Focus();
    }
    private void Shortcut_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is SettingsViewModel model)
            e.Handled = model.RecordKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
    }
    private void Shortcut_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is SettingsViewModel model) model.RecordKey(Key.Escape, ModifierKeys.None);
    }
    private void RemoveSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel model && ((Button)sender).DataContext is BrightnessSchedule schedule)
            model.RemoveSchedule(schedule);
    }
    private void SaveBrightnessAutomation_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel model) return;
        if (HasValidationError(this)) { model.ReportInputError(); return; }
        model.SaveBrightnessAutomation();
    }
    private static bool HasValidationError(DependencyObject element)
    {
        if (Validation.GetHasError(element)) return true;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (HasValidationError(VisualTreeHelper.GetChild(element, index))) return true;
        return false;
    }
}
