using System.Drawing;
using System.Runtime.InteropServices;

namespace Lertaro.Plugins.WindowSwitcher;

// Kept separate so the window geometry and its native calls do not push WindowMenuOperations over 300 lines.
internal static class WindowPositioning
{
    internal static bool Apply(IntPtr hwnd, bool fitToScreen)
    {
        var state = WindowMenuOperations.ReadState(hwnd);
        if (!state.IsValid) return false;

        // Borrowed HWND/HMONITOR values: neither belongs to this plugin and neither may be closed.
        // Choose the monitor before restoring; MonitorFromWindow uses a minimized window's saved bounds.
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref info)) return false;
        var workArea = info.WorkArea.ToRectangle();
        if (workArea.Width <= 0 || workArea.Height <= 0) return false;

        if (state.IsMinimized || state.IsMaximized) WindowMenuOperations.Restore(hwnd);
        if (!GetWindowRect(hwnd, out var rect)) return false;
        var window = rect.ToRectangle();
        if (window.Width <= 0 || window.Height <= 0) return false;

        // The host is PerMonitorV2 DPI-aware, so all bounds and the 12-pixel margin use physical pixels.
        var target = CalculateBounds(window, workArea, fitToScreen);
        var flags = SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder | SwpNoSendChanging;
        if (target.Size == window.Size) flags |= SwpNoSize;
        // Skip WM_WINDOWPOSCHANGING so native size limits do not undo the requested fit. Keep focus,
        // topmost state and stacking order; centering alone must never ask Windows to resize the window.
        return SetWindowPos(hwnd, IntPtr.Zero, target.X, target.Y, target.Width, target.Height, flags);
    }

    internal static Rectangle CalculateBounds(Rectangle window, Rectangle workArea, bool fitToScreen)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(window.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(window.Height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workArea.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workArea.Height);

        var width = fitToScreen && window.Width > workArea.Width ? Math.Max(1, workArea.Width - 12) : window.Width;
        var height = fitToScreen && window.Height > workArea.Height ? Math.Max(1, workArea.Height - 12) : window.Height;
        return new Rectangle(workArea.Left + (workArea.Width - width) / 2,
            workArea.Top + (workArea.Height - height) / 2, width, height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect MonitorArea, WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpNoSendChanging = 0x0400;
}
