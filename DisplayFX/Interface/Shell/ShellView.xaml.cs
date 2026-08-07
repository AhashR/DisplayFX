using System;
using System.ComponentModel;
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
using Application = System.Windows.Application;
using System.Windows.Interop;
using System.Windows.Input;

using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;

namespace DisplayFX.Interface.Shell;

public partial class ShellView
{
    private NotifyIcon? _notifyIcon;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

    public ShellView()
    {
        InitializeComponent();
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
    }

    private void ApplyNativeWindowIcon(IntPtr hwnd)
    {
        try
        {
            IntPtr hIcon = IntPtr.Zero;
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "desktop.ico");
            if (System.IO.File.Exists(iconPath))
            {
                using var icon = new System.Drawing.Icon(iconPath);
                hIcon = icon.Handle;
            }
            else
            {
                using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (sysIcon != null) hIcon = sysIcon.Handle;
            }

            if (hIcon != IntPtr.Zero)
            {
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, hIcon);
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, hIcon);
            }
        }
        catch { }
    }

    private void Start()
    {
        IoC.BuildUp(this);

        CreateSystemTrayIcon();

        GlobalEvents.UpdateToolTip += OnUpdateToolTip;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (DataContext is ShellViewModel viewModel && viewModel.Computer.IsStartMinimized)
        {
            Hide();
            Bootstrapper.TrimMemory();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is ShellViewModel viewModel && viewModel.Computer.IsMinimizeToTray)
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
                _notifyIcon.Icon = new Icon(iconPath);
            }
            else
            {
                var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/desktop.ico"))?.Stream;
                _notifyIcon.Icon = iconStream != null ? new Icon(iconStream) : SystemIcons.Application;
            }

            _notifyIcon.Visible = true;

            // show the window when left clicking the tray icon
            _notifyIcon.MouseClick += (s, e) => { 
                if (e.Button == MouseButtons.Left) DoShow(); 
            };

            _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
            _notifyIcon.ContextMenuStrip.Items.Add("Show", null, OpenEvent);
            _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _notifyIcon.ContextMenuStrip.Items.Add("Exit", null, ExitEvent);

            BuildToolTip();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load tray icon: {ex.Message}");
        }
    }

    private void BuildToolTip()
    {
        if (DataContext is not ShellViewModel viewModel)
            return;

        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine("DisplayFX");
        foreach (var monitor in viewModel.Computer.Monitors)
        {
            var activeProfile = monitor.Profiles.Single(p => p.IsActive);
            stringBuilder.AppendLine($"{monitor.Name} - {activeProfile.Name}");
        }

        _notifyIcon!.Text = stringBuilder.ToString();
    }

    private void ExitEvent(object? sender, EventArgs args)
    {
        Application.Current.Shutdown();
    }

    private void OpenEvent(object? sender, EventArgs args)
    {
        DoShow();
    }

    public void DoShow()
    {
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
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            Bootstrapper.TrimMemory();
        }

        base.OnStateChanged(e);
    }
}