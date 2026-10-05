using System.Windows.Controls;
using System.Windows;

namespace DisplayFX.Interface.Monitors;

public partial class MonitorView : UserControl
{
    public MonitorView()
    {
        InitializeComponent();
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
