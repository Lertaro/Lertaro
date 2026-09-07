using System.Runtime.InteropServices;

namespace Lertaro.App.Services.QuickPanel;

// Where the panel puts itself, and the P/Invokes that answer that. Split out of QuickPanelManager.cs
// purely to keep that file under the repo's per-file line limit; this is the one part of the manager
// that is about geometry rather than lifetime.
public sealed partial class QuickPanelManager
{
    private const double PanelSideFactor = 0.5;

    private const double MinPanelWidth = 280;
    private const double MinPanelHeight = 200;

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>Docks the panel inside the host window's bottom-right corner.</summary>
    private void PositionAgainst(IntPtr host)
    {
        if (_window == null) return;

        double width;
        double height;
        double targetPhysLeft;
        double targetPhysTop;
        double targetDpiScale;

        if (host != IntPtr.Zero && GetWindowRect(host, out var rect))
        {
            var dpi = GetDpiForWindow(host);
            var screen = Screen.FromHandle(host);
            var wa = screen.WorkingArea;
            targetDpiScale = dpi > 0 ? dpi / 96.0 : 1.0;

            (width, height, targetPhysLeft, targetPhysTop) = CalculatePhysicalDockPosition(
                rect.Left, rect.Top, rect.Right, rect.Bottom,
                dpi,
                wa.Left, wa.Top, wa.Width, wa.Height);
        }
        else
        {
            var mousePos = Control.MousePosition;
            var mouseScreen = Screen.FromPoint(mousePos);
            var mouseWa = mouseScreen.WorkingArea;
            var targetMonitor = MonitorFromPoint(new POINT { X = mousePos.X, Y = mousePos.Y }, MONITOR_DEFAULTTONEAREST);
            var dpi = GetMonitorDpi(targetMonitor);
            targetDpiScale = dpi > 0 ? dpi / 96.0 : 1.0;

            (width, height, targetPhysLeft, targetPhysTop) = CalculatePhysicalDockPosition(
                mouseWa.Left, mouseWa.Top, mouseWa.Right, mouseWa.Bottom,
                dpi,
                mouseWa.Left, mouseWa.Top, mouseWa.Width, mouseWa.Height);
        }

        var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).EnsureHandle();
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, IntPtr.Zero,
                (int)Math.Round(targetPhysLeft), (int)Math.Round(targetPhysTop), 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        _window.Width = width;
        _window.Height = height;
        _window.Left = targetPhysLeft / targetDpiScale;
        _window.Top = targetPhysTop / targetDpiScale;
    }

    internal static (double Width, double Height, double PhysLeft, double PhysTop) CalculatePhysicalDockPosition(
        int hostLeft, int hostTop, int hostRight, int hostBottom,
        uint hostDpi,
        int waLeft, int waTop, int waWidth, int waHeight)
    {
        const double margin = 12.0;
        var hostScale = hostDpi > 0 ? hostDpi / 96.0 : 1.0;

        var hostWidthDip = (hostRight - hostLeft) / hostScale;
        var hostHeightDip = (hostBottom - hostTop) / hostScale;

        var panelWidthDip = Math.Max(MinPanelWidth, hostWidthDip * PanelSideFactor);
        var panelHeightDip = Math.Max(MinPanelHeight, hostHeightDip * PanelSideFactor);

        var physPanelWidth = panelWidthDip * hostScale;
        var physPanelHeight = panelHeightDip * hostScale;
        var physMargin = margin * hostScale;

        var targetPhysLeft = hostRight - physPanelWidth - physMargin;
        var targetPhysTop = hostBottom - physPanelHeight - physMargin;

        if (waWidth > 0 && waHeight > 0)
        {
            targetPhysLeft = Math.Clamp(targetPhysLeft, waLeft, waLeft + waWidth - physPanelWidth);
            targetPhysTop = Math.Clamp(targetPhysTop, waTop, waTop + waHeight - physPanelHeight);
        }

        return (panelWidthDip, panelHeightDip, targetPhysLeft, targetPhysTop);
    }

    internal static (double Width, double Height, double Left, double Top) CalculateDockPosition(
        int hostLeft, int hostTop, int hostRight, int hostBottom,
        uint hostDpi,
        int waLeft, int waTop, int waWidth, int waHeight,
        double currentDpiScaleX, double currentDpiScaleY)
    {
        var (width, height, physLeft, physTop) = CalculatePhysicalDockPosition(
            hostLeft, hostTop, hostRight, hostBottom,
            hostDpi,
            waLeft, waTop, waWidth, waHeight);

        var scaleX = currentDpiScaleX > 0 ? currentDpiScaleX : 1.0;
        var scaleY = currentDpiScaleY > 0 ? currentDpiScaleY : 1.0;

        return (width, height, physLeft / scaleX, physTop / scaleY);
    }

    private static uint GetMonitorDpi(IntPtr hMonitor)
    {
        if (hMonitor != IntPtr.Zero && GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0 && dpiX > 0)
            return dpiX;
        return 96;
    }
}
