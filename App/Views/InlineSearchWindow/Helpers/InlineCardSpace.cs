using System.Windows.Interop;
using System.Windows.Media;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

/// <summary>
/// The screen side of the inline card's height budget: which monitor the card has to share, and how much of
/// it the card may take.
/// </summary>
/// <remarks>
/// Split out purely to keep InlineCardSizingSupport under the repo's per-file line limit; this class has no
/// state of its own, it always answers for the one window it is handed. The arithmetic itself lives in
/// <see cref="InlineCardMetrics.AvailableCardHeight"/> so it stays testable -- what needs a live desktop is
/// only reading the screen, the DPI and the anchored window's rectangle.
/// </remarks>
internal static class InlineCardSpace
{
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
