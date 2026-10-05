using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using Caliburn.Micro;
using DisplayFX.Bootstrap;
using DisplayFX.Global;
using DisplayFX.Global.Controllers;
using DisplayFX.Global.Extensions;
using DisplayFX.Interface.BrightnessFlyout;
using Application = System.Windows.Application;
using System.Windows.Interop;
using System.Windows.Input;

using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;

namespace DisplayFX.Interface.Shell;

public partial class ShellView
{
    private NotifyIcon? _notifyIcon;
    private BrightnessFlyoutView? _brightnessFlyout;
    private readonly HashSet<BrightnessFlyoutView> _brightnessWindows = new();
    private bool _flyoutWasOpenOnMouseDown;
    private bool _startupOptionsApplied;
    private bool _isExiting;
    private Icon? _nativeIcon;
    private Icon? _trayIcon;
    private DisplayPowerNotifications? _powerNotifications;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

    public ShellView()
    {
        InitializeComponent();
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 16);
        SetWindowIcon();
        Start();
    }

    private void SetWindowIcon()
    {
        try
        {
            var pngUri = new Uri("pack://application:,,,/DisplayFX;component/Resources/desktop.png", UriKind.RelativeOrAbsolute);
            Icon = BitmapFrame.Create(pngUri);
        }
        catch
        {
            try
            {
                var iconUri = new Uri("pack://application:,,,/DisplayFX;component/Resources/desktop.ico", UriKind.RelativeOrAbsolute);
                Icon = BitmapFrame.Create(iconUri);
            }
            catch { }
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        // add message handler to listen for messages from other instances of the app
        HwndSource source = HwndSource.FromHwnd(hwnd);
        source.AddHook(WndProc);

        ApplyNativeWindowIcon(hwnd);
        _powerNotifications = new DisplayPowerNotifications(hwnd, awake => IoC.Get<MonitorBrightnessController>().NotifyDisplayPower(awake));
    }

    private void ApplyNativeWindowIcon(IntPtr hwnd)
    {
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "desktop.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _nativeIcon = new Icon(iconPath);
            }
            else
            {
                _nativeIcon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            }

            if (_nativeIcon != null)
            {
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, _nativeIcon.Handle);
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, _nativeIcon.Handle);
            }
        }
        catch { }
    }

    private void Start()
    {
        IoC.BuildUp(this);

        CreateSystemTrayIcon();

        GlobalEvents.UpdateToolTip += OnUpdateToolTip;
        DataContextChanged += (_, _) => BuildToolTip();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (_startupOptionsApplied)
            return;
        _startupOptionsApplied = true;
        if (DataContext is ShellViewModel viewModel && viewModel.Computer.IsStartMinimized)
        {
            if (viewModel.Computer.IsMinimizeToTray && _notifyIcon?.Visible == true)
                Hide();
            else
                WindowState = WindowState.Minimized;
            Bootstrapper.TrimMemory();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExiting && _notifyIcon?.Visible == true &&
            DataContext is ShellViewModel viewModel && viewModel.Computer.IsMinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            Bootstrapper.TrimMemory();
            return;
        }

        base.OnClosing(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // listen for the custom message and show the window contents
        if (msg == 0x0400 + 1) // WM_SHOWME
        {
            DoShow();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnUpdateToolTip()
    {
        BuildToolTip();
    }

    private void CreateSystemTrayIcon()
    {
        try
        {
            _notifyIcon = new NotifyIcon();

            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "desktop.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _trayIcon = new Icon(iconPath);
            }
            else
            {
                using var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/desktop.ico"))?.Stream;
                _trayIcon = iconStream != null ? new Icon(iconStream) : (Icon)SystemIcons.Application.Clone();
            }

            _notifyIcon.Icon = _trayIcon;
            _notifyIcon.Visible = true;

            // Left-click toggles the brightness flyout.
            _notifyIcon.MouseDown += OnNotifyIconMouseDown;
            _notifyIcon.MouseClick += OnNotifyIconMouseClick;

            _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
            _notifyIcon.ContextMenuStrip.Items.Add("Adjust brightness", null, BrightnessEvent);
            _notifyIcon.ContextMenuStrip.Items.Add("Show menu", null, OpenEvent);
            _notifyIcon.ContextMenuStrip.Items.Add("Identify displays", null, (_, _) =>
            {
                if (DataContext is ShellViewModel model) model.IdentifyMonitors();
            });
            _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _notifyIcon.ContextMenuStrip.Items.Add("Exit", null, ExitEvent);

            BuildToolTip();
        }
        catch (Exception ex)
        {
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            _trayIcon?.Dispose();
            _trayIcon = null;
            System.Diagnostics.Debug.WriteLine($"Failed to load tray icon: {ex.Message}");
        }
    }

    private void BuildToolTip()
    {
        if (_notifyIcon == null || DataContext is not ShellViewModel viewModel)
            return;

        _notifyIcon.Text = CreateToolTipText(viewModel.Computer);
    }

    internal static string CreateToolTipText(DisplayFX.Objects.Entities.Computer computer)
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine("DisplayFX");
        foreach (var monitor in computer.Monitors)
        {
            var activeProfile = monitor.Profiles.FirstOrDefault(p => p.IsActive);
            stringBuilder.AppendLine($"{monitor.DisplayName} - {activeProfile?.Name ?? "No active profile"}");
        }

        var text = stringBuilder.ToString().TrimEnd();
        return text.Length > 127 ? text[..124] + "..." : text;
    }

    private async void ExitEvent(object? sender, EventArgs args)
    {
        _isExiting = true;
        var flyouts = _brightnessWindows.ToArray();
        foreach (var flyout in flyouts) flyout.Close();
        await Task.WhenAll(flyouts.Select(flyout => flyout.CloseCompletion));
        Application.Current.Shutdown();
    }

    private void OpenEvent(object? sender, EventArgs args)
    {
        DoShow();
    }

    private void BrightnessEvent(object? sender, EventArgs args)
    {
        ShowBrightnessFlyout();
    }

    private void OnNotifyIconMouseDown(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        // Snapshot the flyout state before this click can deactivate (and
        // thereby close) it, so the click handler can distinguish open vs close.
        _flyoutWasOpenOnMouseDown = _brightnessFlyout is { IsVisible: true };
    }

    private void OnNotifyIconMouseClick(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        // A single left click simply toggles the brightness flyout.
        if (_flyoutWasOpenOnMouseDown)
            CloseBrightnessFlyout();
        else
            ShowBrightnessFlyout();
    }

    private async void ShowBrightnessFlyout()
    {
        await Task.WhenAll(_brightnessWindows.Where(window => !window.IsVisible).Select(window => window.CloseCompletion));
        if (_isExiting) return;
        // Reuse the window if it is already on screen instead of stacking a
        // second one on top of it.
        if (_brightnessFlyout is { IsVisible: true })
        {
            _brightnessFlyout.Activate();
            return;
        }

        // A previous flyout may still be finishing its close (click-away closes
        // it through Deactivated); drop the stale reference before opening anew.
        _brightnessFlyout = null;

        BrightnessFlyoutViewModel viewModel;
        try
        {
            viewModel = IoC.Get<BrightnessFlyoutViewModel>();
        }
        catch (Exception exception)
        {
            IoC.Get<NLog.ILogger>().Warn(exception, "Failed to load the brightness flyout.");
            System.Windows.MessageBox.Show("Unable to load monitor brightness controls. Check the display connection and try again.",
                "DisplayFX", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        viewModel.CloseRequested = CloseBrightnessFlyout;
        viewModel.MainAppRequested = () => { CloseBrightnessFlyout(); DoShow(); };
        viewModel.SettingsRequested = async () =>
        {
            var closingFlyout = _brightnessFlyout;
            CloseBrightnessFlyout();
            if (closingFlyout != null) await closingFlyout.CloseCompletion;
            DoShow();
            if (DataContext is ShellViewModel shellViewModel)
                await shellViewModel.OpenSettings();
        };

        var flyout = new BrightnessFlyoutView { DataContext = viewModel };
        _brightnessWindows.Add(flyout);
        flyout.Closed += (_, _) =>
        {
            _brightnessWindows.Remove(flyout);
            if (ReferenceEquals(_brightnessFlyout, flyout))
                _brightnessFlyout = null;
        };

        _brightnessFlyout = flyout;
        flyout.Show();
        flyout.Activate();
    }

    private void CloseBrightnessFlyout()
    {
        var flyout = _brightnessFlyout;
        _brightnessFlyout = null;

        if (flyout is not { IsVisible: true })
            return;

        try
        {
            flyout.Close();
        }
        catch (InvalidOperationException)
        {
            // Already closing (e.g. Deactivated began the close) — nothing to do.
        }
    }

    public void DoShow()
    {
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 16);
        Show();
        WindowState = WindowState.Normal;

        // ensure to focus the window so that it brings it to the front
        Activate();
        Focus();

        // toggle topmost to bring it to the front above all other windows
        // then disable so it behaves normally
        Topmost = true;
        Topmost = false;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _notifyIcon?.Visible == true &&
            DataContext is ShellViewModel viewModel && viewModel.Computer.IsMinimizeToTray)
        {
            Hide();
            Bootstrapper.TrimMemory();
        }

        base.OnStateChanged(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        GlobalEvents.UpdateToolTip -= OnUpdateToolTip;
        _powerNotifications?.Dispose();
        CloseBrightnessFlyout();
        _notifyIcon?.ContextMenuStrip?.Dispose();
        _notifyIcon?.Dispose();
        _trayIcon?.Dispose();
        _nativeIcon?.Dispose();
        base.OnClosed(e);
    }
}
