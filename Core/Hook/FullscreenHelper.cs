using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Core.Hook;

/// <summary>
/// Checks whether the current foreground window is running in full-screen mode.
/// Lives in Core so it can be used by both the hook sub-process and the App.
/// </summary>
public static class FullscreenHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    public static bool IsForegroundWindowFullScreen()
    {
        try
        {
            var fgHwnd = GetForegroundWindow();
            if (fgHwnd == IntPtr.Zero) return false;

            // Ignore Desktop/Tray windows
            var sbClass = new StringBuilder(256);
            GetClassName(fgHwnd, sbClass, sbClass.Capacity);
            var cls = sbClass.ToString();
            if (cls.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("WorkerW", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (GetVisibleWindowRect(fgHwnd, out var rect))
            {
                var hMonitor = MonitorFromWindow(fgHwnd, MONITOR_DEFAULTTOPRIMARY);
                var mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(mi);
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    const int tolerance = 2;
                    return Math.Abs(rect.Left - mi.rcMonitor.Left) <= tolerance &&
                           Math.Abs(rect.Top - mi.rcMonitor.Top) <= tolerance &&
                           Math.Abs(rect.Right - mi.rcMonitor.Right) <= tolerance &&
                           Math.Abs(rect.Bottom - mi.rcMonitor.Bottom) <= tolerance;
                }
            }
        }
        catch { }
        return false;
    }

    private static bool GetVisibleWindowRect(IntPtr hwnd, out RECT rect)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) == 0)
        {
            return true;
        }

        return GetWindowRect(hwnd, out rect);
    }
}
