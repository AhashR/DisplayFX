using System.Collections.Generic;

namespace DisplayFX.Objects.Entities;

public class Computer
{
    public bool IsStartWithWindows { get; set; }
    public bool IsApplySettingsOnStart { get; set; }
    public bool IsStartMinimized { get; set; }
    public bool IsMinimizeToTray { get; set; }
    public List<Monitor> Monitors { get; set; } = new();
}