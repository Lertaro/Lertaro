using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using Lertaro.Core;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

public class QuickSearchWindowPositioner
{
    [DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

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
        // Target monitor and placement must come from the monitor the mouse cursor is currently on.
        var mousePos = Control.MousePosition;
        var targetMonitor = MonitorFromPoint(new POINT { X = mousePos.X, Y = mousePos.Y }, MONITOR_DEFAULTTONEAREST);
        var (dpiScaleX, _) = GetMonitorDpiScale(targetMonitor);
        var targetDpiFactorX = dpiScaleX > 0 ? 1.0 / dpiScaleX : 1.0;

        var screen = Screen.FromPoint(mousePos);
        var wa = screen.WorkingArea;
        var settings = UserSettings.Load();
        var windowWidth = settings.SearchWindow.SearchBarWidth + 48;

        // WPF applies Window.Left/Top by multiplying the DIP value by the window's CURRENT monitor DPI scale.
        // When moving across monitors with different DPIs, we must compensate using the window's current DPI scale
        // so the underlying Win32 SetWindowPos receives the exact target physical coordinates.
        var currentDpi = VisualTreeHelper.GetDpi(_window);
        var (left, top) = CalculatePosition(
            wa.Left, wa.Top, wa.Width, wa.Height,
            windowWidth, targetDpiFactorX,
            currentDpi.DpiScaleX, currentDpi.DpiScaleY,
            settings.SearchWindow.RelativeLeft, settings.SearchWindow.RelativeTop);

        _window.Left = left;
        _window.Top = top;
    }

    internal static (double Left, double Top) CalculatePosition(
        int waLeft, int waTop, int waWidth, int waHeight,
        double windowDipWidth, double targetDpiFactorX,
        double currentDpiScaleX, double currentDpiScaleY,
        double? relativeLeft, double? relativeTop)
    {
        double targetPhysX;
        double targetPhysY;

        if (relativeLeft.HasValue && relativeTop.HasValue)
        {
            var relLeft = Math.Clamp(relativeLeft.Value, -0.5, 1.0);
            var relTop = Math.Clamp(relativeTop.Value, 0.0, 0.9);
            targetPhysX = waLeft + relLeft * waWidth;
            targetPhysY = waTop + relTop * waHeight;
        }
        else
        {
            var targetPhysWidth = windowDipWidth * targetDpiFactorX;
            targetPhysX = waLeft + (waWidth - targetPhysWidth) / 2.0;
            targetPhysY = waTop + waHeight * 0.22;
        }

        var scaleX = currentDpiScaleX > 0 ? currentDpiScaleX : 1.0;
        var scaleY = currentDpiScaleY > 0 ? currentDpiScaleY : 1.0;
        return (targetPhysX / scaleX, targetPhysY / scaleY);
    }

    // Wired to QuickSearchWindow's drag handler right after a drag finishes moving the window.
    // Records where it ended up as a fraction of whichever monitor it is now on.
    public void SaveWindowPosition()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var screen = Screen.FromHandle(hwnd);
        var wa = screen.WorkingArea;
        if (wa.Width <= 0 || wa.Height <= 0)
            return;

        var currentDpi = VisualTreeHelper.GetDpi(_window);
        var scaleX = currentDpi.DpiScaleX > 0 ? currentDpi.DpiScaleX : 1.0;
        var scaleY = currentDpi.DpiScaleY > 0 ? currentDpi.DpiScaleY : 1.0;

        var physLeft = _window.Left * scaleX;
        var physTop = _window.Top * scaleY;

        var settings = UserSettings.Load();
        settings.SearchWindow.RelativeLeft = (physLeft - wa.Left) / wa.Width;
        settings.SearchWindow.RelativeTop = (physTop - wa.Top) / wa.Height;
        settings.Save();
    }

    public void ResetPosition()
    {
        var settings = UserSettings.Load();
        settings.SearchWindow.RelativeLeft = null;
        settings.SearchWindow.RelativeTop = null;
        settings.Save();
        PositionWindow();
    }

    private static (double x, double y) GetMonitorDpiScale(IntPtr hMonitor)
    {
        if (hMonitor != IntPtr.Zero && GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0 && dpiX > 0 && dpiY > 0)
            return (96.0 / dpiX, 96.0 / dpiY);
        return (1.0, 1.0);
    }
}
