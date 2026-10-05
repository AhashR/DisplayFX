using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

/// <summary>Exercises real window handles off screen, without moving the pointer or touching the running app.</summary>
internal static class WindowMovementChecks
{
    public static int Run(params Window[] windows)
    {
        var count = 0;
        foreach (var window in windows)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Opacity = 0;
            window.Left = -20000;
            window.Top = -20000;
            window.Show();
            window.UpdateLayout();
            var handle = new WindowInteropHelper(window).Handle;
            if (!GetWindowRect(handle, out var before)) throw new InvalidOperationException("Cannot inspect window geometry.");
            var captionPoint = PointParameter((before.Left + before.Right) / 2, before.Top + 15);
            var hit = SendMessage(handle, 0x0084, IntPtr.Zero, captionPoint).ToInt32(); // WM_NCHITTEST
            if (hit != 2) throw new InvalidOperationException($"{window.Title}: title-bar hit test returned {hit}, expected HTCAPTION (2).");
            count++;
            var moveState = GetMenuState(GetSystemMenu(handle, false), 0xF010, 0); // SC_MOVE, MF_BYCOMMAND
            if (moveState == uint.MaxValue || (moveState & 3) != 0)
                throw new InvalidOperationException($"{window.Title}: Windows has disabled title-bar movement.");
            count++;
            if (!SetWindowPos(handle, IntPtr.Zero, before.Left + 32, before.Top + 24, 0, 0, 0x15)) // no resize, reorder or activate
                throw new InvalidOperationException($"{window.Title}: Windows could not move the window.");
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if (!GetWindowRect(handle, out var after) || after.Left != before.Left + 32 || after.Top != before.Top + 24)
                throw new InvalidOperationException($"{window.Title}: window position did not change.");
            count++;
            window.Hide();
            window.Close();
        }
        return count;
    }

    private static IntPtr PointParameter(int x, int y) => new(unchecked((y << 16) | (x & 0xFFFF)));
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool revert);
    [DllImport("user32.dll")] private static extern uint GetMenuState(IntPtr menu, uint command, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
