using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

/// <summary>
/// The screen side of the inline card: which monitor it is on, that monitor's DPI scale, the working area it
/// has to stay inside, and how much vertical room it may take.
/// </summary>
/// <remarks>
/// Split out purely to keep the two consumers (InlineCardSizingSupport and InlineSearchWindowPositioner)
/// under the repo's per-file line limit -- and so they cannot disagree about which screen they are talking
/// about; the classes themselves have no state and always answer for the one window they are handed. The
/// arithmetic lives in <see cref="InlineCardMetrics"/> so it stays testable; what needs a live desktop is only
/// reading the screen, the DPI and the anchored window's rectangle.
/// </remarks>
internal static class InlineCardSpace
{
    private const int MdtEffectiveDpi = 0;
    private const uint MonitorDefaultToNearest = 2;

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    /// <summary>The monitor a window is on, for the DPI that window is being rendered at.</summary>
    internal static IntPtr MonitorForWindow(IntPtr hwnd) => MonitorFromWindow(hwnd, MonitorDefaultToNearest);

    /// <summary>The monitor a point is on, used before this window has a handle to ask about.</summary>
    internal static IntPtr MonitorForPoint(Point point) =>
        MonitorFromPoint(new POINT { X = point.X, Y = point.Y }, MonitorDefaultToNearest);

    /// <summary>The monitor's effective DPI as a scale factor, 1.0 when it cannot be read.</summary>
    internal static (double X, double Y) DpiScaleFor(IntPtr monitor)
    {
        if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out var dpiY) == 0 && dpiX > 0 && dpiY > 0)
            return (dpiX / 96.0, dpiY / 96.0);
        return (1.0, 1.0);
    }

    /// <summary>
    /// The working area the card is being docked inside: the anchored window's monitor when there is one, this
    /// window's own otherwise (or the mouse's, before this window has a handle). Empty when there is neither.
    /// </summary>
    internal static Rectangle WorkingAreaFor(Lertaro.App.InlineSearchWindow window, Point mousePosition)
    {
        var tracker = window.Manager.ExplorerTracker;
        var hwnd = new WindowInteropHelper(window).Handle;

        if (tracker.IsDesktop)
        {
            var screen = window.IsVisible && hwnd != IntPtr.Zero ? Screen.FromHandle(hwnd) : Screen.FromPoint(mousePosition);
            return screen.WorkingArea;
        }

        return tracker.ActiveHwnd != IntPtr.Zero ? Screen.FromHandle(tracker.ActiveHwnd).WorkingArea : Rectangle.Empty;
    }

    /// <summary>How much vertical room the card has right now, in DIP.</summary>
    /// <remarks>
    /// The monitor is the ANCHORED window's when there is one -- that is the screen this card has to share
    /// with it -- and this window's own otherwise. This window's DPI converts the physical working area back
    /// to DIP: the positioner places the card on the anchored window's monitor, so the two agree once it has
    /// settled, and a single wrong pass before that only re-sizes the card once.
    /// </remarks>
    internal static double AvailableHeight(Lertaro.App.InlineSearchWindow window)
    {
        var tracker = window.Manager.ExplorerTracker;
        var dpiScaleY = VisualTreeHelper.GetDpi(window).DpiScaleY;
        if (dpiScaleY <= 0) dpiScaleY = 1.0;

        var hwnd = new WindowInteropHelper(window).Handle;
        var screen = tracker.ActiveHwnd != IntPtr.Zero
            ? Screen.FromHandle(tracker.ActiveHwnd)
            : hwnd != IntPtr.Zero
                ? Screen.FromHandle(hwnd)
                : Screen.FromPoint(Control.MousePosition);

        double activeWindowHeight = 0;
        double spaceBelow = 0;
        if (tracker.ActiveHwnd != IntPtr.Zero && !tracker.IsDesktop
            && tracker.TryGetActiveWindowRect(out var rect)
            && rect.Bottom - rect.Top > 100 && rect.Right - rect.Left > 100)
        {
            activeWindowHeight = (rect.Bottom - rect.Top) / dpiScaleY;
            spaceBelow = (screen.WorkingArea.Bottom - rect.Bottom) / dpiScaleY;
        }

        return InlineCardMetrics.AvailableCardHeight(screen.WorkingArea.Height / dpiScaleY, activeWindowHeight, spaceBelow);
    }
}
