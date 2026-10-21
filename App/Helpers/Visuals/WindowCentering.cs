using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Lertaro.App.Views.InlineSearchWindow.Helpers;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Centers a window in a screen's work area, in that screen's own scale.
/// </summary>
/// <remarks>
/// Split out because four windows need the same thing (the full search window, the settings window and both
/// LocalSend ones) and because the framework's own answer cannot do it: WPF reads the cursor's monitor correctly
/// but sizes the window with the scale a never-shown window holds -- the system's -- and the window is resized to
/// the monitor's scale only afterwards, so it aims at the center of a smaller window than the one that lands on
/// screen. Measured on a 100% primary with a 250% secondary and 854x480 DIP: rect (3093,768) at 2135x1200 against
/// that work area's own center (2453,408), off by +641,+360 = exactly half the size difference, toward the
/// bottom-right. Equal-scale desktops make the delta zero, which is why this survived.
/// </remarks>
internal static class WindowCentering
{
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>
    /// Where a window of the given DIP size goes to sit in the middle of a work area given in physical pixels,
    /// answered in DIP.
    /// </summary>
    /// <remarks>
    /// The two units are why this is a function with its own tests rather than two lines at the call site: the
    /// screen is read in pixels while Left/Top are DIP, so the window's size is scaled up to be centered and the
    /// answer scaled back down, and on a mixed-DPI desktop the scale is whichever monitor the window is going to.
    /// </remarks>
    internal static (double Left, double Top) CenterInWork(
        int workLeft, int workTop, int workWidth, int workHeight,
        double windowDipWidth, double windowDipHeight,
        double dpiScaleX, double dpiScaleY)
    {
        var scaleX = dpiScaleX > 0 ? dpiScaleX : 1.0;
        var scaleY = dpiScaleY > 0 ? dpiScaleY : 1.0;

        // Maxed against the area's own edge: a window larger than the area cannot be centered in it, and
        // letting the arithmetic go negative would push the header -- the only thing that can drag the window
        // back -- off screen.
        var physLeft = Math.Max(workLeft, workLeft + (workWidth - windowDipWidth * scaleX) / 2.0);
        var physTop = Math.Max(workTop, workTop + (workHeight - windowDipHeight * scaleY) / 2.0);
        return (physLeft / scaleX, physTop / scaleY);
    }

    /// <summary>
    /// Puts the window in the middle of the work area of the screen the mouse is on -- the screen a summon came
    /// from, and the one its own surface already targets. Safe before Show and after: only the physical pass needs
    /// a handle, so the caller can run it once from <see cref="WindowParking.ParkUntilPainted"/>, where the window
    /// has had a handle for the first time and nothing has been painted yet.
    /// </summary>
    internal static void CenterOnCursorScreen(Window window)
    {
        var mouse = Control.MousePosition;
        var work = Screen.FromPoint(mouse).WorkingArea;
        var (scaleX, scaleY) = InlineCardSpace.DpiScaleFor(InlineCardSpace.MonitorForPoint(mouse));

        // Manual: whatever the markup said, this is the placement now, and a CenterScreen/CenterOwner startup
        // location would re-assert itself over it at Show.
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        var (left, top) = CenterInWork(work.Left, work.Top, work.Width, work.Height,
            window.Width, window.Height, scaleX, scaleY);
        window.Left = left;
        window.Top = top;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // The DIP write alone is not enough: WPF converts a window that has never been shown with the SYSTEM
        // scale, so measured on the same desktop that put the window at (981,163) -- on the primary, at 1x size,
        // when the cursor was on the 250% secondary. Physical pixels are what actually lands it.
        SetWindowPos(hwnd, IntPtr.Zero,
            (int)Math.Round(left * scaleX), (int)Math.Round(top * scaleY), 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }
}
