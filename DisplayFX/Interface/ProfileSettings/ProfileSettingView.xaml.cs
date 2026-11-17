using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Input;

namespace DisplayFX.Interface.ProfileSettings;

public partial class ProfileSettingView : UserControl
{
    public ProfileSettingView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _originalValueText = null;
            _discardValue = false;
        };
    }

    private string? _originalValueText;
    private bool _discardValue;

    private void ExactValue_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var editor = (TextBox)sender;
        if (!editor.IsKeyboardFocusWithin && editor.Focus()) e.Handled = true;
    }

    private void ExactValue_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var editor = (TextBox)sender;
        if (editor.Text.TrimEnd().EndsWith('%'))
            editor.SetCurrentValue(TextBox.TextProperty, editor.Text.TrimEnd().TrimEnd('%').TrimEnd());
        _originalValueText = editor.Text;
        editor.SelectAll();
    }

    private void ExactValue_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var editor = (TextBox)sender;
        if (!_discardValue && _originalValueText != null) CommitExactValue(editor);
        editor.ClearValue(Control.BorderBrushProperty);
        editor.ClearValue(FrameworkElement.ToolTipProperty);
        editor.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        _originalValueText = null;
        _discardValue = false;
    }

    private void ExactValue_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        var editor = (TextBox)sender;
        if (e.Key == Key.Enter && !CommitExactValue(editor)) { e.Handled = true; return; }
        _discardValue = true;
        editor.ClearValue(Control.BorderBrushProperty);
        editor.ClearValue(FrameworkElement.ToolTipProperty);
        editor.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        if (!editor.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)))
        {
            _discardValue = false;
            _originalValueText = editor.Text;
            editor.SelectAll();
        }
        e.Handled = true;
    }

    private bool CommitExactValue(TextBox editor)
    {
        if (DataContext is not ProfileSettingViewModel model || editor.Tag is not string channel) return false;
        var valid = editor.Text == _originalValueText || model.TrySetExactColorValue(channel, editor.Text);
        if (!valid && editor.IsKeyboardFocused)
        {
            editor.BorderBrush = System.Windows.Media.Brushes.IndianRed;
            editor.ToolTip = channel == "Gamma" ? "Enter a gamma value from 0.4 to 2.8." : "Enter a percentage from 0 to 100.";
            editor.SelectAll();
            return false;
        }
        editor.ClearValue(Control.BorderBrushProperty);
        editor.ClearValue(FrameworkElement.ToolTipProperty);
        editor.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        return valid;
    }

}

public sealed class ExactColorValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double number) return "";
        return parameter is "Gamma"
            ? number.ToString("0.00", CultureInfo.CurrentCulture)
            : (number * 100).ToString("0.##", CultureInfo.CurrentCulture) + "%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
