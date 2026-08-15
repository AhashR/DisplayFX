using System;
using System.Windows;
using System.Windows.Input;

namespace DisplayFX.Interface.BrightnessFlyout;

/// <summary>
///     Borderless popup window hosting the per-monitor brightness controls. It
///     positions itself above the taskbar near the system tray and closes when it
///     loses focus, mirroring the Windows volume flyout / Twinkle Tray behavior.
/// </summary>
public partial class BrightnessFlyoutView : Window
{
    public BrightnessFlyoutView()
    {
        InitializeComponent();
        ContentRendered += OnContentRendered;
        Deactivated += OnDeactivated;
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        PositionNearTaskbar();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        Close();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key == Key.Escape)
            Close();
    }

    private void PositionNearTaskbar()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 12;
        Top = workArea.Bottom - ActualHeight - 12;
    }

    private void TurnOffMonitors_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is BrightnessFlyoutViewModel viewModel)
            viewModel.TurnOffMonitors();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is BrightnessFlyoutViewModel viewModel)
            viewModel.OpenSettings();
    }
}
