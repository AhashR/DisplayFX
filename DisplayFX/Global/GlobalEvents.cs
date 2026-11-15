using System;

namespace DisplayFX.Global;

public static class GlobalEvents
{
    public static Action? UpdateToolTip { get; set; } = delegate { };
    public static Action<string>? NotificationRequested { get; set; }
}
