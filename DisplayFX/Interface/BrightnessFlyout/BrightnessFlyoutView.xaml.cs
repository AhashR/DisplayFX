using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DisplayFX.Interface.BrightnessFlyout;

/// <summary>
///     Borderless popup window hosting the per-monitor brightness controls. It
///     positions itself above the taskbar near the system tray and closes when it
///     loses focus, mirroring the Windows volume flyout / Twinkle Tray behavior.
/// </summary>
public partial class BrightnessFlyoutView : Window
{
    public System.Drawing.Point AnchorPoint { get; set; } = System.Windows.Forms.Cursor.Position;
    private bool _isClosing;
    private bool _canClose;
    private bool _positioning;
    private readonly TaskCompletionSource _closed = new();
    public Task CloseCompletion => _closed.Task;

    public BrightnessFlyoutView()
    {
        InitializeComponent();
        ContentRendered += OnContentRendered;
        Closing += OnClosing;
        Deactivated += OnDeactivated;
        SizeChanged += (_, _) => { if (IsLoaded) PositionNearTaskbar(); };
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
        if (!_isClosing && DataContext is not BrightnessFlyoutViewModel { IsPinned: true })
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
        if (_positioning) return;
        _positioning = true;
        try
        {
        var area = System.Windows.Forms.Screen.FromPoint(AnchorPoint).WorkingArea;
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(handle, IntPtr.Zero, area.Left + 12, area.Top + 12, 0, 0, 0x0015);
        var scale = GetDpiForWindow(handle) / 96d;
        if (scale <= 0) scale = 1;
        MaxHeight = System.Math.Max(100, area.Height / scale - 24);
        Width = System.Math.Min(320, System.Math.Max(180, area.Width / scale - 24));
        var width = (int)System.Math.Ceiling(ActualWidth * scale);
        var height = (int)System.Math.Ceiling(ActualHeight * scale);
        var x = System.Math.Clamp(AnchorPoint.X - width + 20, area.Left, System.Math.Max(area.Left, area.Right - width));
        var y = AnchorPoint.Y > area.Top + area.Height / 2 ? System.Math.Max(area.Top, area.Bottom - height - 12) : area.Top + 12;
        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0015);
        }
        finally { _positioning = false; }
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

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
