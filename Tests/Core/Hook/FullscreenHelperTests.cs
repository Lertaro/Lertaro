using Lertaro.Core.Hook;
using RECT = Lertaro.Core.Hook.FullscreenHelper.RECT;

namespace Lertaro.Core.Tests.Hook;

[TestClass]
public sealed class FullscreenHelperTests
{
    private const int WS_CAPTION = 0x00C00000, WS_BORDER = 0x00800000, WS_POPUP = unchecked((int)0x80000000), WS_THICKFRAME = 0x00040000;
    private const int Titled = WS_CAPTION | WS_THICKFRAME;

    private static readonly RECT Monitor = Rect(0, 0, 3840, 2160);
    private static readonly RECT WorkWithTaskbar = Rect(0, 0, 3840, 2064);

    // A 1920x1200 display with its taskbar showing, measured on this machine: a maximized window reports
    // 0,0,1920,1152 through DWM and -8,-8,1928,1160 through GetWindowRect.
    private static readonly RECT SmallerMonitor = Rect(0, 0, 1920, 1200);
    private static readonly RECT SmallerWork = Rect(0, 0, 1920, 1152);
    private static readonly RECT InflatedMaximized = Rect(-8, -8, 1928, 1160);

    // Taskbar only on the primary: the secondary's work area is its whole rect.
    private static readonly RECT SecondaryMonitor = Rect(-1920, 0, 0, 1080);

    private static RECT Rect(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };

    // The bug: with the taskbar auto-hidden the work area is the whole monitor, so a maximized Edge or Explorer
    // fills it exactly (measured: rect and work area both 0,0,3840,2160).
    [TestMethod]
    public void IsOrdinaryMaximized_MaximizedOverAnAutoHiddenTaskbar_IsOrdinary() =>
        Assert.IsTrue(FullscreenHelper.IsOrdinaryMaximized(isZoomed: true, Titled, Monitor, work: Monitor));

    [TestMethod]
    public void IsOrdinaryMaximized_MaximizedAboveAVisibleTaskbar_IsOrdinary() =>
        Assert.IsTrue(FullscreenHelper.IsOrdinaryMaximized(isZoomed: true, Titled, WorkWithTaskbar, WorkWithTaskbar));

    // Some apps do their own fullscreen by staying maximized with a title bar and growing over the taskbar.
    [TestMethod]
    public void IsOrdinaryMaximized_MaximizedPastTheWorkArea_IsNotOrdinary() =>
        Assert.IsFalse(FullscreenHelper.IsOrdinaryMaximized(isZoomed: true, Titled, Monitor, WorkWithTaskbar));

    // A borderless popup that maximizes is sized to the monitor itself, and Edge in F11 is maximized without a
    // title bar (measured: style 0x170B0000): both are fullscreen.
    [TestMethod]
    public void IsOrdinaryMaximized_MaximizedCaptionless_IsNotOrdinary() =>
        Assert.IsFalse(FullscreenHelper.IsOrdinaryMaximized(isZoomed: true, WS_POPUP, Monitor, Monitor));

    // WS_CAPTION is two bits; a thin border alone is not a title bar.
    [TestMethod]
    public void IsOrdinaryMaximized_MaximizedWithBorderOnly_IsNotOrdinary() =>
        Assert.IsFalse(FullscreenHelper.IsOrdinaryMaximized(isZoomed: true, WS_POPUP | WS_BORDER, Monitor, Monitor));

    [TestMethod]
    public void IsOrdinaryMaximized_NotMaximized_IsNotOrdinary() =>
        Assert.IsFalse(FullscreenHelper.IsOrdinaryMaximized(isZoomed: false, Titled, Monitor, Monitor));

    // The resize border a maximized window keeps around its visible bounds: without the slack the GetWindowRect
    // fallback would miss the work area by the border width and read every maximized window as fullscreen again.
    [TestMethod]
    public void IsOrdinaryMaximized_InflatedFallbackRect_NeedsTheBorderSlack()
    {
        Assert.IsFalse(FullscreenHelper.IsOrdinaryMaximized(isZoomed: true, Titled, InflatedMaximized, SmallerWork));
        Assert.IsTrue(FullscreenHelper.IsOrdinaryMaximized(isZoomed: true, Titled, InflatedMaximized, SmallerWork, slack: 10));
    }

    // The tolerance belongs to the slack, not to a bigger window: two pixels off the work area still counts.
    [TestMethod]
    public void IsOrdinaryMaximized_WorkAreaBoundaryWithinTolerance_IsOrdinary()
    {
        Assert.IsTrue(FullscreenHelper.IsOrdinaryMaximized(isZoomed: true, Titled, Rect(-2, -2, 1922, 1154), SmallerWork));
        Assert.IsFalse(FullscreenHelper.IsOrdinaryMaximized(isZoomed: true, Titled, Rect(-3, -3, 1923, 1155), SmallerWork));
    }

    // The verdict, not just the predicate: a wrong monitor or work area passed down here now fails a test.
    [TestMethod]
    public void IsFullScreen_MaximizedTitledOverAnAutoHiddenTaskbar_IsNotFullscreen() =>
        Assert.IsFalse(FullscreenHelper.IsFullScreen(Monitor, Monitor, Monitor, isZoomed: true, Titled, slack: 2));

    // Regression guard: with a visible taskbar this never read as fullscreen before the fix, and it still does not.
    [TestMethod]
    public void IsFullScreen_MaximizedTitledAboveAVisibleTaskbar_IsNotFullscreen() =>
        Assert.IsFalse(FullscreenHelper.IsFullScreen(WorkWithTaskbar, Monitor, WorkWithTaskbar, isZoomed: true, Titled, slack: 2));

    [TestMethod]
    public void IsFullScreen_CaptionlessMaximizedFillingTheMonitor_StaysFullscreen() =>
        Assert.IsTrue(FullscreenHelper.IsFullScreen(Monitor, Monitor, Monitor, isZoomed: true, WS_POPUP, slack: 2));

    // F11 in a browser or a borderless game window: not maximized, fills the monitor, still fullscreen.
    [TestMethod]
    public void IsFullScreen_UnmaximizedWindowFillingTheMonitor_StaysFullscreen() =>
        Assert.IsTrue(FullscreenHelper.IsFullScreen(Monitor, Monitor, Monitor, isZoomed: false, Titled, slack: 2));

    [TestMethod]
    public void IsFullScreen_MaximizedTitledPastTheWorkArea_StaysFullscreen() =>
        Assert.IsTrue(FullscreenHelper.IsFullScreen(Monitor, Monitor, WorkWithTaskbar, isZoomed: true, Titled, slack: 2));

    [TestMethod]
    public void IsFullScreen_RestoredWindowInsideTheMonitor_IsNotFullscreen() =>
        Assert.IsFalse(FullscreenHelper.IsFullScreen(Rect(100, 100, 1000, 800), Monitor, WorkWithTaskbar, isZoomed: false, Titled, slack: 2));

    // A maximized window on a monitor without a taskbar is the same bug, at a negative origin.
    [TestMethod]
    public void IsFullScreen_MaximizedTitledOnASecondaryMonitor_IsNotFullscreen() =>
        Assert.IsFalse(FullscreenHelper.IsFullScreen(SecondaryMonitor, SecondaryMonitor, SecondaryMonitor, isZoomed: true, Titled, slack: 2));

    [TestMethod]
    public void IsFullScreen_FillsTheMonitorWithinTolerance_IsFullscreen()
    {
        Assert.IsTrue(FullscreenHelper.IsFullScreen(Rect(2, 0, 3840, 2160), Monitor, Monitor, isZoomed: false, Titled, slack: 2));
        Assert.IsFalse(FullscreenHelper.IsFullScreen(Rect(3, 0, 3840, 2160), Monitor, Monitor, isZoomed: false, Titled, slack: 2));
    }
}
