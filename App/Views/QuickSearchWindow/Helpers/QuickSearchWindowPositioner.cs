using System.Runtime.InteropServices;
using System.Windows.Interop;
using Lertaro.Core;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

public class QuickSearchWindowPositioner
{
    [DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private readonly Lertaro.App.QuickSearchWindow _window;
    private readonly Func<IntPtr> _getLastActiveHwnd;

    public QuickSearchWindowPositioner(Lertaro.App.QuickSearchWindow window, Func<IntPtr> getLastActiveHwnd)
    {
        _window = window;
        _getLastActiveHwnd = getLastActiveHwnd;
    }

    public void PositionWindow()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        Screen screen;
        IntPtr targetMonitor;

        // When the window is already visible (e.g. pinned / handling DpiChanged), preserve placement on its current monitor.
        // When summoning (not yet visible), target the monitor under the mouse cursor.
        if (_window.IsVisible && hwnd != IntPtr.Zero)
        {
            screen = Screen.FromHandle(hwnd);
            targetMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        }
        else
        {
            var mousePos = Control.MousePosition;
            screen = Screen.FromPoint(mousePos);
            targetMonitor = MonitorFromPoint(new POINT { X = mousePos.X, Y = mousePos.Y }, MONITOR_DEFAULTTONEAREST);
        }

        var (targetDpiScaleX, targetDpiScaleY) = GetMonitorDpiScale(targetMonitor);
        var wa = screen.WorkingArea;
        var settings = UserSettings.Load();
        var windowWidth = settings.SearchWindow.SearchBarWidth + 48;

        var (targetPhysX, targetPhysY) = CalculatePhysicalPosition(
            wa.Left, wa.Top, wa.Width, wa.Height,
            windowWidth, targetDpiScaleX,
            settings.SearchWindow.RelativeLeft, settings.SearchWindow.RelativeTop);

        // Set WPF DIP properties first so WPF's internal logical coordinates are updated.
        // If WPF's internal DPI scale is temporarily stale (e.g. while hidden across different DPI monitors),
        // WPF's Left/Top setter may calculate an incorrect physical position; calling Win32 SetWindowPos
        // afterwards ensures the HWND is authoritatively placed at the exact target physical coordinates.
        _window.Left = targetPhysX / targetDpiScaleX;
        _window.Top = targetPhysY / targetDpiScaleY;

        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, IntPtr.Zero,
                (int)Math.Round(targetPhysX), (int)Math.Round(targetPhysY), 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    internal static (double PhysX, double PhysY) CalculatePhysicalPosition(
        int waLeft, int waTop, int waWidth, int waHeight,
        double windowDipWidth, double targetDpiScaleX,
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
            var targetPhysWidth = windowDipWidth * targetDpiScaleX;
            targetPhysX = waLeft + (waWidth - targetPhysWidth) / 2.0;
            targetPhysY = waTop + waHeight * 0.22;
        }

        return (targetPhysX, targetPhysY);
    }

    internal static (double Left, double Top) CalculatePosition(
        int waLeft, int waTop, int waWidth, int waHeight,
        double windowDipWidth, double targetDpiScaleX, double targetDpiScaleY,
        double? relativeLeft, double? relativeTop)
    {
        var (physX, physY) = CalculatePhysicalPosition(
            waLeft, waTop, waWidth, waHeight,
            windowDipWidth, targetDpiScaleX,
            relativeLeft, relativeTop);
        var scaleX = targetDpiScaleX > 0 ? targetDpiScaleX : 1.0;
        var scaleY = targetDpiScaleY > 0 ? targetDpiScaleY : 1.0;
        return (physX / scaleX, physY / scaleY);
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

        if (!GetWindowRect(hwnd, out var rect))
            return;

        var settings = UserSettings.Load();
        settings.SearchWindow.RelativeLeft = (double)(rect.Left - wa.Left) / wa.Width;
        settings.SearchWindow.RelativeTop = (double)(rect.Top - wa.Top) / wa.Height;
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
            return (dpiX / 96.0, dpiY / 96.0);
        return (1.0, 1.0);
    }
}
