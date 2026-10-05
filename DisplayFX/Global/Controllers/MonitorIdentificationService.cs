using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DisplayFX.Interface.Monitors;
using NLog;
using FormsScreen = System.Windows.Forms.Screen;

namespace DisplayFX.Global.Controllers;

public sealed class MonitorIdentificationService : IDisposable
{
    private readonly ILogger _logger;
    private readonly List<Window> _windows = new();
    private DispatcherTimer? _closeTimer;

    public MonitorIdentificationService(ILogger logger)
    {
        _logger = logger;
    }

    public void Show(IEnumerable<MonitorViewModel> monitors)
    {
        Close();
        var connected = monitors.ToList();
        for (var index = 0; index < connected.Count; index++)
        {
            var monitor = connected[index];
            try
            {
                var screen = FormsScreen.AllScreens.FirstOrDefault(candidate =>
                    string.Equals(candidate.DeviceName, monitor.ScreenName, StringComparison.OrdinalIgnoreCase));
                if (screen == null) continue;

                var overlay = CreateOverlay(monitor.Name, GetDisplayNumber(screen.DeviceName, index + 1));
                overlay.SourceInitialized += (_, _) =>
                {
                    var handle = new WindowInteropHelper(overlay).Handle;
                    SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000 | 0x00000020);
                };
                overlay.Loaded += (_, _) =>
                {
                    var handle = new WindowInteropHelper(overlay).Handle;
                    // Position in native pixels so negative screen coordinates and different DPI scales work.
                    SetWindowPos(handle, new IntPtr(-1), screen.Bounds.Left + 32, screen.Bounds.Top + 32,
                        0, 0, 0x0010 | 0x0001);
                    var dpi = GetDpiForWindow(handle);
                    var scale = (dpi == 0 ? 96 : dpi) / 96d;
                    var width = (int)Math.Ceiling(overlay.Width * scale);
                    var height = (int)Math.Ceiling(overlay.Height * scale);
                    SetWindowPos(handle, new IntPtr(-1),
                        screen.Bounds.Left + (screen.Bounds.Width - width) / 2,
                        screen.Bounds.Top + (screen.Bounds.Height - height) / 2,
                        0, 0, 0x0010 | 0x0001); // SWP_NOACTIVATE | SWP_NOSIZE
                    overlay.Opacity = 1;
                };
                _windows.Add(overlay);
                overlay.Show();
            }
            catch (Exception exception)
            {
                _logger.Warn(exception, "Could not show a monitor identification label.");
            }
        }

        if (_windows.Count == 0) return;
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _closeTimer.Tick += OnCloseTimerTick;
        _closeTimer.Start();
    }

    internal static int GetDisplayNumber(string screenName, int fallback)
    {
        const string prefix = @"\\.\DISPLAY";
        return screenName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(screenName.Substring(prefix.Length), NumberStyles.None,
                   CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : fallback;
    }

    private static Window CreateOverlay(string name, int number)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 14, 24, 18) };
        panel.Children.Add(new TextBlock
        {
            Text = number.ToString(CultureInfo.InvariantCulture), FontSize = 76, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = name, FontSize = 15, Foreground = Brushes.White, TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "DisplayFX", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(172, 184, 199)),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        return new Window
        {
            Width = 300, Height = 205, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, ShowActivated = false, Topmost = true, AllowsTransparency = true,
            Background = Brushes.Transparent, IsHitTestVisible = false, Opacity = 0,
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(245, 26, 34, 48)), CornerRadius = new CornerRadius(16),
                BorderBrush = new SolidColorBrush(Color.FromRgb(91, 152, 237)), BorderThickness = new Thickness(2),
                Child = panel
            }
        };
    }

    private void OnCloseTimerTick(object? sender, EventArgs e) => Close();

    private void Close()
    {
        if (_closeTimer != null)
        {
            _closeTimer.Stop();
            _closeTimer.Tick -= OnCloseTimerTick;
            _closeTimer = null;
        }
        foreach (var window in _windows)
            window.Close();
        _windows.Clear();
    }

    public void Dispose() => Close();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
