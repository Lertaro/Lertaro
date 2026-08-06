using System.Runtime.InteropServices;
using System.Windows.Interop;
using Lertaro.Core;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

public class QuickSearchWindowPositioner
{
    [DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private readonly Lertaro.App.QuickSearchWindow _window;
    private readonly Func<IntPtr> _getLastActiveHwnd;

    public QuickSearchWindowPositioner(Lertaro.App.QuickSearchWindow window, Func<IntPtr> getLastActiveHwnd)
    {
        _window = window;
        _getLastActiveHwnd = getLastActiveHwnd;
    }

    public void PositionWindow()
    {
        // DPI and placement must come from the monitor the mouse cursor is currently on, ensuring
        // double-Ctrl activation follows the user's active cursor in multi-monitor setups.
        var mousePos = Control.MousePosition;
        var targetMonitor = MonitorFromPoint(new POINT { X = mousePos.X, Y = mousePos.Y }, MONITOR_DEFAULTTONEAREST);
        var (dpiScaleX, dpiScaleY) = GetMonitorDpiScale(targetMonitor);

        var screen = Screen.FromPoint(mousePos);

        var (waLeft, waTop, waWidth, waHeight) = WorkingAreaInDip(screen, dpiScaleX, dpiScaleY);
        var settings = UserSettings.Load();
        var windowWidth = settings.SearchWindow.SearchBarWidth + 48;

        if (settings.SearchWindow.RelativeLeft.HasValue && settings.SearchWindow.RelativeTop.HasValue)
        {
            // Re-derives the equivalent spot on the TARGET monitor (wherever the mouse/foreground window
            // currently is) from a fraction of ITS work area, instead of the absolute pixel position the
            // window was originally dragged to on a possibly completely different monitor -- see
            // SaveWindowPosition for how this fraction was computed. Clamped rather than validated
            // against "is this on some connected monitor" (the old Left/Top-pixel check): a fraction is
            // always meaningful on any monitor by construction, but still clamped in case a monitor swap
            // (e.g. a much smaller target display) would otherwise place most of the window off-screen.
            var relLeft = Math.Clamp(settings.SearchWindow.RelativeLeft.Value, -0.5, 1.0);
            var relTop = Math.Clamp(settings.SearchWindow.RelativeTop.Value, 0.0, 0.9);
            _window.Left = waLeft + relLeft * waWidth;
            _window.Top = waTop + relTop * waHeight;
        }
        else
        {
            _window.Left = waLeft + (waWidth - windowWidth) / 2;
            _window.Top = waTop + waHeight * 0.22;
        }
    }

    // Wired to QuickSearchWindow's own drag handler (Border_MouseLeftButtonUp), right after a drag
    // finishes moving the window -- records where it ended up as a fraction of whichever monitor it's
    // now ON, so a later PositionWindow (possibly targeting a different monitor entirely) can re-derive
    // the equivalent spot there instead of always reopening on this one specific monitor.
    public void SaveWindowPosition()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var (dpiScaleX, dpiScaleY) = GetMonitorDpiScale(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));
        var (waLeft, waTop, waWidth, waHeight) = WorkingAreaInDip(Screen.FromHandle(hwnd), dpiScaleX, dpiScaleY);
        if (waWidth <= 0 || waHeight <= 0)
            return;

        var settings = UserSettings.Load();
        settings.SearchWindow.RelativeLeft = (_window.Left - waLeft) / waWidth;
        settings.SearchWindow.RelativeTop = (_window.Top - waTop) / waHeight;
        settings.Save();
    }

    // Wired to the search box's status icon right-click -- clears the saved position and immediately
    // re-centers the window using the same fallback PositionWindow already falls back to when there's
    // no saved position.
    public void ResetPosition()
    {
        var settings = UserSettings.Load();
        settings.SearchWindow.RelativeLeft = null;
        settings.SearchWindow.RelativeTop = null;
        settings.Save();
        PositionWindow();
    }

    // Falls back to 1.0 (96 DPI, unscaled) if the monitor handle is invalid or the query fails --
    // GetDpiForMonitor has been available since Windows 8.1, so this should only trip on some
    // unexpected edge case, not any supported OS version.
    private static (double x, double y) GetMonitorDpiScale(IntPtr hMonitor)
    {
        if (hMonitor != IntPtr.Zero && GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0 && dpiX > 0 && dpiY > 0)
            return (96.0 / dpiX, 96.0 / dpiY);
        return (1.0, 1.0);
    }

    // Screen.WorkingArea is physical (system-DPI space); scales it to WPF's DIP space with the given
    // monitor's own DPI factor, matching the space Window.Left/Top live in.
    private static (double Left, double Top, double Width, double Height) WorkingAreaInDip(Screen screen, double dpiScaleX, double dpiScaleY)
    {
        var wa = screen.WorkingArea;
        return (wa.Left * dpiScaleX, wa.Top * dpiScaleY, wa.Width * dpiScaleX, wa.Height * dpiScaleY);
    }
}
