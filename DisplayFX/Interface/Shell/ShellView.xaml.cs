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

namespace DisplayFX.Interface.Shell;

public partial class ShellView
{
    private NotifyIcon? _notifyIcon;

    public ShellView()
    {
        InitializeComponent();
        Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // add message handler to listen for messages from other instances of the app
        HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source.AddHook(WndProc);
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