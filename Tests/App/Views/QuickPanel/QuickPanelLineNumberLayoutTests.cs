using Lertaro.App.Views.QuickPanel;

namespace Lertaro.App.Tests.Views.QuickPanel;

[TestClass]
public sealed class QuickPanelLineNumberLayoutTests
{
    [TestMethod]
    public void DigitsFor_UsesTheLargestVisibleNumber()
    {
        Assert.AreEqual(1, QuickPanelLineNumberLayout.DigitsFor(0));
        Assert.AreEqual(2, QuickPanelLineNumberLayout.DigitsFor(42));
        Assert.AreEqual(3, QuickPanelLineNumberLayout.DigitsFor(100));
    }

    [TestMethod]
    public void RowsFor_RoundsUpForTheLastThumbnailRow()
    {
        Assert.AreEqual(0, QuickPanelLineNumberLayout.RowsFor(0, 5));
        Assert.AreEqual(3, QuickPanelLineNumberLayout.RowsFor(11, 5));
        Assert.AreEqual(11, QuickPanelLineNumberLayout.RowsFor(11, 1));
    }

    [TestMethod]
    public void ThumbnailColumnsFor_ReservesTheGutterBeforeSizingTiles()
    {
        Assert.AreEqual(5, QuickPanelLineNumberLayout.ThumbnailColumnsFor(800, 36));
        Assert.AreEqual(3, QuickPanelLineNumberLayout.ThumbnailColumnsFor(380, 36));
    }
}
