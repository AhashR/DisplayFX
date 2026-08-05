using System.Windows.Controls;
using System.Windows.Input;

namespace DisplayFX.Interface.ProfileSettings;

public partial class ProfileSettingView : UserControl
{
    public ProfileSettingView()
    {
        InitializeComponent();
    }

    private void HotkeyRecorderButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ProfileSettingViewModel vm || !vm.IsRecordingHotkey)
            return;

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            vm.IsRecordingHotkey = false;
            return;
        }

        var modifiers = Keyboard.Modifiers;

        // If user pressed a non-modifier key (or combo with modifier), record it
        if (key != Key.LeftCtrl && key != Key.RightCtrl &&
            key != Key.LeftAlt && key != Key.RightAlt &&
            key != Key.LeftShift && key != Key.RightShift &&
            key != Key.LWin && key != Key.RWin)
        {
            vm.SetRecordedHotkey(modifiers, key);
        }
    }
}