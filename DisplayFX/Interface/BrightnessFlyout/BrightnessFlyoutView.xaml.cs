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
///     anchors to the bottom-right of its display's working area and closes
///     when it loses focus.
/// </summary>
public partial class BrightnessFlyoutView : Window
{
    public System.Drawing.Point AnchorPoint { get; set; } = System.Windows.Forms.Cursor.Position;
    private const double EdgeGap = 12;
    private const uint MoveOnly = 0x0015; // SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
    private bool _isClosing;
    private bool _canClose;
    private bool _positioning;
    private readonly TaskCompletionSource _closed = new();
    public Task CloseCompletion => _closed.Task;

    public BrightnessFlyoutView()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            ((HwndSource?)PresentationSource.FromVisual(this))?.AddHook(OnWindowMessage);
            PositionInCorner();
        };
        ContentRendered += (_, _) => PositionInCorner();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(PositionInCorner));
        Closing += OnClosing;
        Deactivated += OnDeactivated;
        SizeChanged += (_, _) => { if (IsLoaded) Dispatcher.BeginInvoke(new Action(PositionInCorner)); };
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

    private IntPtr OnWindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmDisplayChange = 0x007E;
        const int WmSettingChange = 0x001A;
        const int SpiSetWorkArea = 0x002F;
        if (message == WmDisplayChange || (message == WmSettingChange && wParam.ToInt64() == SpiSetWorkArea))
            Dispatcher.BeginInvoke(new Action(PositionInCorner));
        return IntPtr.Zero;
    }

    private void PositionInCorner()
    {
        if (_positioning || _isClosing) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        _positioning = true;
        try
        {
            // The cursor determines the display, not the popup's position on it.
            var screen = System.Windows.Forms.Screen.FromPoint(AnchorPoint);
            var area = screen.WorkingArea;
            // Place the HWND on that display before reading its DPI.
            if (System.Windows.Forms.Screen.FromHandle(handle).DeviceName != screen.DeviceName)
                SetWindowPos(handle, IntPtr.Zero, area.Left, area.Top, 0, 0, MoveOnly);

            var dpi = GetDpiForWindow(handle);
            var scale = dpi == 0 ? 1d : dpi / 96d;
            var gap = (int)Math.Round(EdgeGap * scale);
            Width = Math.Min(320, Math.Max(1, (area.Width - 2 * gap) / scale));
            MaxHeight = Math.Max(1, (area.Height - 2 * gap) / scale);

            // Read the actual window rectangle in the same coordinates as the work area,
            // instead of estimating its native size from WPF's layout dimensions.
            if (!GetWindowRect(handle, out var rect)) return;
            var position = GetCornerPosition(area,
                new System.Drawing.Size(rect.Right - rect.Left, rect.Bottom - rect.Top), gap);
            if (position.X != rect.Left || position.Y != rect.Top)
                SetWindowPos(handle, IntPtr.Zero, position.X, position.Y, 0, 0, MoveOnly);
        }
        finally { _positioning = false; }
    }

    private static System.Drawing.Point GetCornerPosition(System.Drawing.Rectangle area, System.Drawing.Size size, int gap)
        => new(Math.Max(area.Left, area.Right - size.Width - gap),
               Math.Max(area.Top, area.Bottom - size.Height - gap));

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    private void OpenMainApp_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is BrightnessFlyoutViewModel viewModel) viewModel.OpenMainApp();
    }

}
