using System;
using System.ComponentModel;
using System.Threading.Tasks;
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
    private bool _isClosing;
    private bool _canClose;
    private readonly TaskCompletionSource _closed = new();
    public Task CloseCompletion => _closed.Task;

    public BrightnessFlyoutView()
    {
        InitializeComponent();
        ContentRendered += OnContentRendered;
        Closing += OnClosing;
        Deactivated += OnDeactivated;
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        PositionNearTaskbar();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_canClose) return;
        e.Cancel = true;
        if (_isClosing) return;
        _isClosing = true;
        Hide();
        try
        {
            if (DataContext is BrightnessFlyoutViewModel viewModel)
            {
                await viewModel.FlushPendingBrightnessAsync();
                viewModel.Dispose();
            }
        }
        finally { _canClose = true; _ = Dispatcher.BeginInvoke(new Action(Close)); }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed.TrySetResult();
        base.OnClosed(e);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Deactivation also fires while the window is already closing; guard
        // against the re-entrant Close() that would otherwise throw.
        if (!_isClosing)
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
    private void OpenMainApp_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is BrightnessFlyoutViewModel viewModel) viewModel.OpenMainApp();
    }

}
