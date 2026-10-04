using Lertaro.App.Converters;
using Lertaro.App.Views.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.QuickPanel;

// [DoNotParallelize] because ThumbnailColumnsFor answers to the panel's ambient thumbnail size, which
// the tile-metrics tests set while they run. Pinned to ExtraLarge here: the column counts these assert
// are the ones the panel has always shown.
[TestClass]
[DoNotParallelize]
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
        QuickPanelTileMetrics.IconSize = QuickPanelThumbnailSize.ExtraLarge;
        try
        {
            Assert.AreEqual(5, QuickPanelLineNumberLayout.ThumbnailColumnsFor(800, 36));
            Assert.AreEqual(3, QuickPanelLineNumberLayout.ThumbnailColumnsFor(380, 36));
        }
        finally
        {
            QuickPanelTileMetrics.IconSize = QuickPanelThumbnailSize.ExtraLarge;
        }
    }
}
