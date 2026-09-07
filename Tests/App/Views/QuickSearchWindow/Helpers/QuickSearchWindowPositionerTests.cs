using Lertaro.App.Views.QuickSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.QuickSearchWindow.Helpers;

[TestClass]
public sealed class QuickSearchWindowPositionerTests
{
    [TestMethod]
    public void CalculatePosition_SameDpi_CentersCorrectly()
    {
        var (left, top) = QuickSearchWindowPositioner.CalculatePosition(
            waLeft: 0, waTop: 0, waWidth: 1920, waHeight: 1080,
            windowDipWidth: 600, targetDpiFactorX: 1.0,
            currentDpiScaleX: 1.0, currentDpiScaleY: 1.0,
            relativeLeft: null, relativeTop: null);

        Assert.AreEqual(660.0, left, 0.001);
        Assert.AreEqual(237.6, top, 0.001);
    }

    [TestMethod]
    public void CalculatePosition_MixedDpiCrossMonitor_CompensatesCorrectly()
    {
        // Target display: 125% DPI (1.25x), 1080p secondary screen at X=3840
        // Window currently on 150% DPI display (1.5x)
        var (left, top) = QuickSearchWindowPositioner.CalculatePosition(
            waLeft: 3840, waTop: 0, waWidth: 1920, waHeight: 1080,
            windowDipWidth: 648, targetDpiFactorX: 1.25,
            currentDpiScaleX: 1.5, currentDpiScaleY: 1.5,
            relativeLeft: null, relativeTop: null);

        // Target physical width = 648 * 1.25 = 810px
        // Target physical X = 3840 + (1920 - 810) / 2 = 4395px
        // Target physical Y = 1080 * 0.22 = 237.6px
        // Compensated for WPF 1.5x scale: Left = 4395 / 1.5 = 2930, Top = 237.6 / 1.5 = 158.4
        Assert.AreEqual(2930.0, left, 0.001);
        Assert.AreEqual(158.4, top, 0.001);

        // When WPF applies 1.5x on the underlying HWND, it hits exactly 4395 and 237.6 physical pixels
        Assert.AreEqual(4395.0, left * 1.5, 0.001);
        Assert.AreEqual(237.6, top * 1.5, 0.001);
    }

    [TestMethod]
    public void CalculatePosition_RelativePercentage_PreservedAcrossMixedDpi()
    {
        var (left, top) = QuickSearchWindowPositioner.CalculatePosition(
            waLeft: 3840, waTop: 0, waWidth: 1920, waHeight: 1080,
            windowDipWidth: 648, targetDpiFactorX: 1.25,
            currentDpiScaleX: 1.5, currentDpiScaleY: 1.5,
            relativeLeft: 0.2, relativeTop: 0.1);

        // Target physical X = 3840 + 0.2 * 1920 = 4224px
        // Target physical Y = 0 + 0.1 * 1080 = 108px
        Assert.AreEqual(4224.0 / 1.5, left, 0.001);
        Assert.AreEqual(108.0 / 1.5, top, 0.001);
        Assert.AreEqual(4224.0, left * 1.5, 0.001);
        Assert.AreEqual(108.0, top * 1.5, 0.001);
    }
}
