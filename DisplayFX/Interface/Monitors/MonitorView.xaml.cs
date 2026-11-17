using System.Windows.Controls;
using System.Windows;

namespace DisplayFX.Interface.Monitors;

public partial class MonitorView : UserControl
{
    public MonitorView()
    {
        InitializeComponent();
    }

    private void OnMonitorClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MonitorViewModel monitor) monitor.IsSelected = true;
    }

    private void OnSaveNameClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MonitorViewModel monitor)
            monitor.ApplyCustomName();
    }

    private void OnClearNameClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MonitorViewModel monitor)
            monitor.ClearCustomName();
    }
}

// A selected monitor is a tab, not an on/off switch.
public sealed class MonitorTabButton : System.Windows.Controls.Primitives.ToggleButton
{
    protected override void OnToggle() { }
}
