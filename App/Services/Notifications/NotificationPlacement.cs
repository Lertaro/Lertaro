using System.Runtime.InteropServices;
using System.Windows;
using Lertaro.Core.Hook;

namespace Lertaro.App.Services.Notifications;

/// <summary>
/// Which screen a notification goes on and how that screen's working area reads in DIP.
/// </summary>
/// <remarks>
/// The notification follows the foreground window's monitor, because that is where the user is looking;
/// the cursor's monitor is the fallback when there is no foreground window worth naming, and the primary
/// screen the last resort. Sizes are given in DIP and the working area has to arrive in the same unit, or
/// a 175% screen turns a 360 DIP card into a 360 pixel one -- so the scale is read from the monitor the
/// chosen screen actually is, not from what this process was started on.
/// </remarks>
internal static class NotificationPlacement
{
    private const int MdtEffectiveDpi = 0;
    private const uint MonitorDefaultToNearest = 2;

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    internal readonly record struct Target(Rect WorkAreaDip, double ScaleX)
    {
        /// <summary>The x of the work area's right edge, in DIP.</summary>
        public double Right => WorkAreaDip.X + WorkAreaDip.Width;

        /// <summary>The y of the work area's bottom edge, in DIP.</summary>
        public double Bottom => WorkAreaDip.Y + WorkAreaDip.Height;
    }

    /// <summary>Resolves the screen a notification should be placed on, right now.</summary>
    internal static Target Resolve()
    {
        var foreground = ExplorerNativeHooks.GetForegroundWindow();
        Screen? screen;
        IntPtr monitor;

        if (foreground != IntPtr.Zero)
        {
            screen = Screen.FromHandle(foreground);
            monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        }
        else if (GetCursorPos(out var cursor))
        {
            screen = Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
            monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        }
        else
        {
            screen = Screen.PrimaryScreen;
            monitor = screen == null
                ? IntPtr.Zero
                : MonitorFromPoint(new POINT
                {
                    X = screen.Bounds.X + screen.Bounds.Width / 2,
                    Y = screen.Bounds.Y + screen.Bounds.Height / 2
                }, MonitorDefaultToNearest);
        }

        if (screen == null) return new Target(new Rect(0, 0, 0, 0), 1.0);

        var area = screen.WorkingArea;
        var scale = DpiScaleFor(monitor);
        return new Target(
            new Rect(area.X / scale, area.Y / scale, area.Width / scale, area.Height / scale),
            scale);
    }

    /// <summary>The monitor's horizontal DPI as a scale factor, 1.0 when there is no monitor to ask.</summary>
    private static double DpiScaleFor(IntPtr monitor)
    {
        if (monitor != IntPtr.Zero
            && GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0
            && dpiX > 0)
            return dpiX / 96.0;
        return 1.0;
    }
}
