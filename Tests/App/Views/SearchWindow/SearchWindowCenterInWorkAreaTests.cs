using Lertaro.App.Views.SearchWindow;

namespace Lertaro.App.Tests.Views.SearchWindow;

// The logo's right-click puts the whole window in the middle of the screen it is on. The window itself can't
// be asserted from a test, but the arithmetic can: the screen is read in physical pixels while Window.Left/Top
// are DIP, so the size is scaled up to be centered and the answer scaled back down with the scale of whichever
// monitor the window happens to be on.
[TestClass]
public sealed class SearchWindowCenterInWorkAreaTests
{
    [TestMethod]
    public void CalculateCenter_CentersWithinTheWorkAreaNotTheScreen()
    {
        // A 1080 tall monitor with the taskbar taking the bottom 140px: the centering line is the work area's,
        // so the window must not be centered against the full 1080.
        var (left, top) = SearchWindowChromeHandler.CalculateCenter(
            workLeft: 0, workTop: 0, workWidth: 1920, workHeight: 940,
            windowDipWidth: 800, windowDipHeight: 600, dpiScaleX: 1.0, dpiScaleY: 1.0);
        Assert.AreEqual(560, left, 0.001);
        Assert.AreEqual(170, top, 0.001);
    }

    [TestMethod]
    public void CalculateCenter_OffsetsByTheWorkAreasOwnOrigin()
    {
        // Secondary monitor to the right of a 1920 wide primary...
        Assert.AreEqual(3840 + 560, SearchWindowChromeHandler.CalculateCenter(
            3840, 0, 1920, 1080, 800, 600, 1.0, 1.0).Left, 0.001);

        // ... and one to the left of it, whose coordinates are negative.
        Assert.AreEqual(-1920 + 560, SearchWindowChromeHandler.CalculateCenter(
            -1920, 0, 1920, 1080, 800, 600, 1.0, 1.0).Left, 0.001);
    }

    [TestMethod]
    public void CalculateCenter_MixedDpi_ScalesUpToCenterAndBackDownToDip()
    {
        // A 150% secondary screen at X=1920: 800x600 DIP is 1200x900 physical, so it centers at 360,90 physical,
        // which is 2280,90 in this monitor's own physical coordinates and 1520,60 in the DIP the window is told.
        var (left, top) = SearchWindowChromeHandler.CalculateCenter(
            workLeft: 1920, workTop: 0, workWidth: 1920, workHeight: 1080,
            windowDipWidth: 800, windowDipHeight: 600, dpiScaleX: 1.5, dpiScaleY: 1.5);
        Assert.AreEqual(1520, left, 0.001);
        Assert.AreEqual(60, top, 0.001);
    }

    [TestMethod]
    public void CalculateCenter_NeverCentersTheHeaderOffScreen()
    {
        // A window taller than the area cannot be centered in it; it has to land on the area's top edge rather
        // than half a window above it, because the header is the only thing that can drag it back.
        var (left, top) = SearchWindowChromeHandler.CalculateCenter(
            workLeft: 0, workTop: 100, workWidth: 1000, workHeight: 500,
            windowDipWidth: 1200, windowDipHeight: 900, dpiScaleX: 1.0, dpiScaleY: 1.0);
        Assert.AreEqual(0, left, 0.001);
        Assert.AreEqual(100, top, 0.001);
    }
}
