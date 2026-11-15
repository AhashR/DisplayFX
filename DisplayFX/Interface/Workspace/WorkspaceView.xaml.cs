using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DisplayFX.Interface.Workspace;
public partial class WorkspaceView : Window
{
    public WorkspaceView()
    {
        InitializeComponent();
        MaxHeight = System.Math.Max(480, SystemParameters.WorkArea.Height - 32);
        MaxWidth = System.Math.Max(640, SystemParameters.WorkArea.Width - 32);
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is WorkspaceViewModel { IsBusy: true }) { e.Cancel = true; return; }
        base.OnClosing(e);
    }
    private void SavePreferences_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceViewModel model)
        {
            if (HasValidationError(this)) { model.ReportInputError(); return; }
            model.SavePreferences();
        }
    }
    private void SaveOther_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel model) return;
        if (HasValidationError(this)) { model.ReportInputError(); return; }
        switch (((Button)sender).Tag as string)
        {
            case "SaveMetadata": model.SaveMetadata(); break;
            case "SaveAutomation": model.SaveAutomation(); break;
            case "SaveMonitorSettings": model.SaveMonitorSettings(); break;
        }
    }
    private static bool HasValidationError(DependencyObject element)
    {
        if (Validation.GetHasError(element)) return true;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++) if (HasValidationError(VisualTreeHelper.GetChild(element, index))) return true;
        return false;
    }
}
