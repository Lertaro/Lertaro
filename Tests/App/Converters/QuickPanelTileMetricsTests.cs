using System.Globalization;
using System.Windows;
using Lertaro.App.Converters;
using Lertaro.Core;

namespace Lertaro.App.Tests.Converters;

// How wide a tile is, and how big the picture in it is, for a list of a given width.
//
// [DoNotParallelize] because the size under test is an ambient the panel sets per open: these tests
// set it themselves, and any test reading it concurrently would answer for whichever size was current.
[TestClass]
[DoNotParallelize]
public sealed class QuickPanelTileMetricsTests
{
    private static double Slot(double listWidth) => (double)new QuickPanelTileMetrics().Convert(
        listWidth, typeof(double), null!, CultureInfo.InvariantCulture);

    private static double Icon(double listWidth) => (double)new QuickPanelTileMetrics().Convert(
        listWidth, typeof(double), "Icon", CultureInfo.InvariantCulture);

    private static int Columns(double listWidth) => (int)(listWidth / Slot(listWidth));

    /// <summary>Runs one assertion under one thumbnail size, then puts the ambient back the way it was.</summary>
    private static void AtSize(QuickPanelThumbnailSize size, Action run)
    {
        QuickPanelTileMetrics.IconSize = size;
        try
        {
            run();
        }
        finally
        {
            QuickPanelTileMetrics.IconSize = QuickPanelThumbnailSize.ExtraLarge;
        }
    }

    // Every width, at every plausible panel size, at every thumbnail size the settings offer: the tiles
    // divide the row rather than being handed out in fixed lumps, so what is left over is never enough
    // for another one. An empty strip at the end of a row is the one thing that reads as a mistake, and
    // is worth tiles a few pixels smaller. The smaller sizes move both ends the count answers to -- the
    // wide end's ceiling and the narrow end's floor -- so the row has to stay full against whichever
    // ceiling and floor are current.
    [TestMethod]
    public void NoWidthEverLeavesRoomForAnotherTileAtTheEndOfTheRow()
    {
        foreach (var size in new[] { QuickPanelThumbnailSize.Small, QuickPanelThumbnailSize.Medium, QuickPanelThumbnailSize.Large, QuickPanelThumbnailSize.ExtraLarge })
        {
            AtSize(size, () =>
            {
                for (var width = 200.0; width < 3000; width += 7.3)
                {
                    var slot = Slot(width);
                    var columns = Columns(width);
                    var leftover = width - columns * slot;
                    Assert.IsLessThan(slot, leftover, $"a {width:F0}-wide list wastes {leftover:F0} of it");
                    // The picture's own floor plus the chrome: below this the tile is too small to hold
                    // its name, whatever size the user picked.
                    Assert.IsGreaterThanOrEqualTo(72, slot, $"at {width:F0}");
                    // The ceiling governs while the row is taking more tiles rather than fewer: five or
                    // more can only have come from the width asking for them, so none may be padded
                    // past it. Four or fewer is the narrow floor dividing the width among what is left,
                    // which may spend more on a tile than the ceiling allows -- fewer, bigger tiles is
                    // what the narrow end is for.
                    if (columns >= QuickPanelTileMetrics.Columns)
                        Assert.IsLessThanOrEqualTo(QuickPanelTileMetrics.MaxSlot, slot, $"at {width:F0}");
                }
            });
        }
    }

    // The whole point: a wide panel spends the space on bigger thumbnails, not just more of them.
    [TestMethod]
    public void AnOrdinaryPanel_DividesItsWidthByFive()
    {
        Assert.AreEqual(160, Slot(800));
        Assert.AreEqual(5, Columns(800));
    }

    // Past where a picture can use the width, the row takes another tile instead of padding five --
    // at every size, each against its own ceiling.
    [TestMethod]
    public void AVeryWidePanel_TakesMoreThanFive()
    {
        foreach (var size in new[] { QuickPanelThumbnailSize.Small, QuickPanelThumbnailSize.Medium, QuickPanelThumbnailSize.Large, QuickPanelThumbnailSize.ExtraLarge })
        {
            AtSize(size, () =>
            {
                Assert.IsGreaterThan(QuickPanelTileMetrics.Columns, Columns(2000));
                Assert.IsLessThanOrEqualTo(QuickPanelTileMetrics.MaxSlot, Slot(2000));
            });
        }
    }

    // And below where a tile is worth looking at, fewer than five -- still dividing the width, so the
    // row is full at four rather than five unreadable ones with a gap after them.
    [TestMethod]
    public void ANarrowPanel_TakesFewerThanFive()
    {
        Assert.AreEqual(4, Columns(380));
        Assert.IsGreaterThanOrEqualTo(92, Slot(380));
    }

    [TestMethod]
    public void TheIconLeavesRoomForTheNameUnderIt()
        => Assert.IsLessThan(Slot(800), Icon(800), "a picture filling the slot would push the name out of the tile");

    // Every tile gets the same cell, so a row of mixed content stays a row. Letting each picture keep
    // its own height made a row as tall as its tallest member, which came out ragged the moment a
    // square icon sat next to a thumbnail.
    [TestMethod]
    public void ThePictureBoxIsWiderThanItIsTall()
    {
        var box = (double)new QuickPanelTileMetrics().Convert(
            800.0, typeof(double), "IconHeight", CultureInfo.InvariantCulture);

        Assert.IsLessThan(Icon(800), box, "square is what left a band of empty tile around 16:9 thumbnails");
        Assert.IsGreaterThan(Icon(800) * 0.5625, box, "and a 16:9 picture should very nearly fill it");
    }

    [TestMethod]
    public void TheCellIsThePictureBoxPlusRoomForTheName()
    {
        var box = (double)new QuickPanelTileMetrics().Convert(
            800.0, typeof(double), "IconHeight", CultureInfo.InvariantCulture);
        var cell = (double)new QuickPanelTileMetrics().Convert(
            800.0, typeof(double), "Cell", CultureInfo.InvariantCulture);

        Assert.IsGreaterThan(box, cell);
    }

    [TestMethod]
    public void AListThatHasNotBeenMeasuredYet_SetsNothing()
    {
        Assert.AreEqual(DependencyProperty.UnsetValue, new QuickPanelTileMetrics().Convert(
            double.NaN, typeof(double), null!, CultureInfo.InvariantCulture));
        Assert.AreEqual(DependencyProperty.UnsetValue, new QuickPanelTileMetrics().Convert(
            0.0, typeof(double), null!, CultureInfo.InvariantCulture));
    }

    // The setting's whole point: visibly different tiles at the same width, ordered as the dropdown
    // names them. ExtraLarge's numbers here are the ones the panel had before the setting existed,
    // which is what makes it the default nobody's panel changes for.
    [TestMethod]
    public void TheSizesComeOutOrderedAtAnOrdinaryWidth()
    {
        var small = 0.0;
        var medium = 0.0;
        var large = 0.0;
        var extraLarge = 0.0;

        AtSize(QuickPanelThumbnailSize.Small, () => small = Slot(800));
        AtSize(QuickPanelThumbnailSize.Medium, () => medium = Slot(800));
        AtSize(QuickPanelThumbnailSize.Large, () => large = Slot(800));
        AtSize(QuickPanelThumbnailSize.ExtraLarge, () => extraLarge = Slot(800));

        Assert.IsLessThan(medium, small);
        Assert.IsLessThan(large, medium);
        Assert.IsLessThan(extraLarge, large);
        Assert.AreEqual(160, extraLarge);
        Assert.AreEqual(5, Columns(800));
    }

    // And the picture inside the tile follows the size it was set to, not just the slot around it.
    // The last line is also the restore check: by then the ambient is back at ExtraLarge, where the
    // rest of this class expects to find it.
    [TestMethod]
    public void ThePictureFollowsTheChosenSize()
    {
        var medium = 0.0;
        var large = 0.0;

        AtSize(QuickPanelThumbnailSize.Medium, () => medium = Icon(800));
        AtSize(QuickPanelThumbnailSize.Large, () => large = Icon(800));

        Assert.IsLessThan(large, medium);
        Assert.AreEqual(136, Icon(800));
    }
}
