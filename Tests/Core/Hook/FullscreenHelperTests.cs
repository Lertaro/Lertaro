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
}
