using System.Collections.Generic;
using System.Windows.Input;

namespace DisplayFX.Objects.Entities;

public class Computer
{
    public bool IsStartWithWindows { get; set; }
    public bool IsApplySettingsOnStart { get; set; }
    public bool IsRememberBrightnessOnStart { get; set; }
    public bool IsStartMinimized { get; set; }
    public bool IsMinimizeToTray { get; set; }
    public bool IsBrightnessLinked { get; set; }
    public bool IsBrightnessShortcutsEnabled { get; set; }
    public int BrightnessShortcutStep { get; set; } = 10;
    public ModifierKeys? BrightnessIncreaseModifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;
    public ModifierKeys? BrightnessDecreaseModifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;
    public Key? BrightnessIncreaseKey { get; set; } = Key.PageUp;
    public Key? BrightnessDecreaseKey { get; set; } = Key.PageDown;
    public bool IsBrightnessScheduleEnabled { get; set; }
    public List<BrightnessSchedule> BrightnessSchedules { get; set; } = new();
    public List<Monitor> Monitors { get; set; } = new();
}
