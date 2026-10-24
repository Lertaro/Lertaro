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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
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
    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int Tolerance = 2;
    private const int SM_CXSIZEFRAME = 32;
    private const int SM_CXPADDEDBORDER = 92;

    // What a maximized window's GetWindowRect adds around its visible bounds on each side. Read once: the
    // fallback path only needs an upper bound, and the value does not change while the process runs.
    private static readonly int BorderSlack = GetSystemMetrics(SM_CXSIZEFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);

    // Reused because the notification queue reads this on its way to a refill, on the UI thread, and a fresh
    // 256-character buffer per read is a Gen0 allocation that never says anything new. The capacity is the
    // documented limit for a class name, so a real one is never truncated into a different answer.
    [ThreadStatic] private static StringBuilder? _className;

    public static bool IsForegroundWindowFullScreen()
    {
        try
        {
            var fgHwnd = GetForegroundWindow();
            if (fgHwnd == IntPtr.Zero) return false;

            // Ignore Desktop/Tray windows
            var sbClass = _className ??= new StringBuilder(256);
            sbClass.Clear();
            GetClassName(fgHwnd, sbClass, sbClass.Capacity);
            var cls = sbClass.ToString();
            if (cls.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("WorkerW", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (GetVisibleWindowRect(fgHwnd, out var rect, out var visibleByDwm))
            {
                var hMonitor = MonitorFromWindow(fgHwnd, MONITOR_DEFAULTTOPRIMARY);
                var mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(mi);
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    // The style is only read once the window is known to be maximized: this runs inside the
                    // keyboard hook callback on every key event, so the common case stays one user32 call.
                    var isZoomed = IsZoomed(fgHwnd);
                    var style = isZoomed ? GetWindowLong(fgHwnd, GWL_STYLE) : 0;

                    // DWM's visible bounds leave out the resize border; the GetWindowRect fallback keeps it, so
                    // that path gets the frame width back as slack, or every maximized window would miss its
                    // work area by the border size and be read as fullscreen again.
                    var slack = visibleByDwm ? Tolerance : Tolerance + BorderSlack;
                    return IsFullScreen(rect, mi.rcMonitor, mi.rcWork, isZoomed, style, slack);
                }
            }
        }
        catch { }
        return false;
    }

    // A maximized window with a title bar that stays inside the work area is an ordinary one the user enlarged.
    // With an auto-hidden taskbar (or on a monitor without one) the work area is the whole monitor, so by size
    // alone a maximized Edge or Explorer read as fullscreen, and silenced hotkeys and notification cards.
    // Still fullscreen: a captionless one (Windows sizes a maximized borderless popup to the monitor itself, and
    // Edge in F11 is maximized without a title bar), and one that grows past the work area over a visible
    // taskbar, which is how some apps do their own fullscreen. ponytail: a style-and-size heuristic -- an app
    // that strips WS_CAPTION to draw its own title bar still reads as fullscreen when maximized over an
    // auto-hidden taskbar. SHQueryUserNotificationState (QUNS_BUSY) is the system's own "fullscreen app
    // running" signal, but it answers for the whole session, not this window, so it could only corroborate.
    internal static bool IsOrdinaryMaximized(bool isZoomed, int style, RECT window, RECT work, int slack = Tolerance) =>
        isZoomed && (style & WS_CAPTION) == WS_CAPTION &&
        window.Left >= work.Left - slack && window.Top >= work.Top - slack &&
        window.Right <= work.Right + slack && window.Bottom <= work.Bottom + slack;

    // The whole verdict in one place, so the tests can pin the wiring as well as the predicate: everything the
    // P/Invoke side reads comes in as plain values, and nothing here can throw.
    internal static bool IsFullScreen(RECT window, RECT monitor, RECT work, bool isZoomed, int style, int slack) =>
        !IsOrdinaryMaximized(isZoomed, style, window, work, slack) && FillsMonitor(window, monitor);

    private static bool FillsMonitor(RECT window, RECT monitor) =>
        Math.Abs(window.Left - monitor.Left) <= Tolerance &&
        Math.Abs(window.Top - monitor.Top) <= Tolerance &&
        Math.Abs(window.Right - monitor.Right) <= Tolerance &&
        Math.Abs(window.Bottom - monitor.Bottom) <= Tolerance;

    private static bool GetVisibleWindowRect(IntPtr hwnd, out RECT rect, out bool fromDwm)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) == 0)
        {
            fromDwm = true;
            return true;
        }

        fromDwm = false;
        return GetWindowRect(hwnd, out rect);
    }
}
