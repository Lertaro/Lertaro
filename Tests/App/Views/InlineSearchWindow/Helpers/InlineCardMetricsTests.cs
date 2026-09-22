using Lertaro.App.Services;
using Lertaro.App.Views.InlineSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

// The inline card's geometry. The card shows up to a fixed number of list rows -- 9, matching the
// Ctrl+1..9 jump range -- and trims to what actually came back once a search is not running.
//
// The point of the layout arithmetic is that section titles must not make the bounded card grow to a tenth
// row. They are ordinary list rows for sizing purposes and are kept only when a result follows them.
[TestClass]
public sealed class InlineCardMetricsTests
{
    private static double RowHeight => UiMetrics.InlineRowHeight;

    // isHeader describes the bound items in display order: true for a section title, false for a result.
    private static bool[] Items(params bool[] isHeader) => isHeader;

    [TestMethod]
    public void ComputeLayout_NoHeaders_ShowsTheWholeBudget()
    {
        var layout = InlineCardMetrics.ComputeLayout(Items(false, false, false), isSearching: true);

        Assert.AreEqual(3, layout.ShownItems);
        Assert.AreEqual(9, layout.AreaRows, "the area keeps the full result budget while searching");
    }

    [TestMethod]
    public void ComputeLayout_WithHeaders_AreaStaysWithinTheBudget()
    {
        // The reported case: a "Current Folder" title, its results, then a "Global Search" title and more.
        var items = Items(false, false, false, true, false, false);
        var layout = InlineCardMetrics.ComputeLayout(items, isSearching: true);

        Assert.AreEqual(9, layout.AreaRows);
        Assert.AreEqual(6, layout.ShownItems, "all five results plus their title");
    }

    [TestMethod]
    public void ComputeLayout_HeadersDoNotMakeTheListTaller()
    {
        // Nine plain rows versus the same bounded list split across two titles. The second list must not grow
        // just because category titles are present.
        var plain = new bool[9];
        var plainLayout = InlineCardMetrics.ComputeLayout(plain, isSearching: true);

        var withHeaders = new bool[11];
        withHeaders[0] = true;   // "Current Folder"
        withHeaders[5] = true;   // "Global Search"
        var splitLayout = InlineCardMetrics.ComputeLayout(withHeaders, isSearching: true);

        // Both lists occupy at most nine rows. The split list contains seven results and two titles.
        Assert.AreEqual(9, plainLayout.ShownItems);
        Assert.AreEqual(9, splitLayout.ShownItems, "two titles share the bounded list rows");
        Assert.AreEqual(9, plainLayout.AreaRows);
        Assert.AreEqual(9, splitLayout.AreaRows);
    }

    [TestMethod]
    public void ComputeLayout_Settled_TrimsToWhatCameBack()
    {
        var layout = InlineCardMetrics.ComputeLayout(Items(false, false), isSearching: false);

        Assert.AreEqual(2, layout.ShownItems);
        Assert.AreEqual(2, layout.AreaRows);
    }

    [TestMethod]
    public void ComputeLayout_Empty_OccupiesNothing()
    {
        // No results at all: the card is just its search box, with no stray title either.
        var layout = InlineCardMetrics.ComputeLayout(Items(), isSearching: false);

        Assert.AreEqual(0, layout.ShownItems);
        Assert.AreEqual(0, layout.AreaRows);
    }

    [TestMethod]
    public void ComputeLayout_TrailingHeaderWithNoResultsUnderIt_IsNotCounted()
    {
        // Two results, then a title whose own section is empty (possible while a search streams in). The
        // title must not be drawn as a heading with nothing beneath it.
        var layout = InlineCardMetrics.ComputeLayout(Items(false, false, true), isSearching: false);

        Assert.AreEqual(2, layout.ShownItems, "the trailing title is excluded");
        Assert.AreEqual(2, layout.AreaRows);
    }

    [TestMethod]
    public void ComputeLayout_MoreResultsThanTheBudget_CapsTheVisibleItems()
    {
        // Settled with far more results than fit: the walk stops after the budget, so the area does not
        // grow with the result count.
        var items = new bool[40];
        var layout = InlineCardMetrics.ComputeLayout(items, isSearching: false);

        Assert.AreEqual(9, layout.ShownItems);
        Assert.AreEqual(9, layout.AreaRows);
    }

    [TestMethod]
    public void ComputeLayout_WhileSearching_IsNeverTallerThanTheBudget()
    {
        // Guards the property that keeps a streaming search from resizing the card: the area is capped by
        // the total list-row budget, never by how many results or titles have arrived.
        for (var results = 0; results <= 30; results++)
        {
            var items = new bool[results + 2];
            items[0] = true;
            items[1] = true;
            var layout = InlineCardMetrics.ComputeLayout(items, isSearching: true);

            Assert.IsLessThanOrEqualTo(InlineCardMetrics.DefaultRows, layout.AreaRows);
            Assert.IsGreaterThanOrEqualTo(layout.ShownItems, layout.AreaRows);
        }
    }

    [TestMethod]
    public void ResultsAreaHeight_IsRowsTimesRowHeight()
    {
        Assert.AreEqual(RowHeight * 9, InlineCardMetrics.ResultsAreaHeight(9));
        Assert.AreEqual(0, InlineCardMetrics.ResultsAreaHeight(0));
        Assert.AreEqual(0, InlineCardMetrics.ResultsAreaHeight(-3));
    }

    // ── The screen-aware budget ──
    // How many rows the card gets is decided by the space it has, not by the fixed nine-row range alone.
    // These are the arithmetic cases; which screen/window those numbers come from is read from the live
    // desktop in InlineCardSizingSupport and cannot be unit tested.
    private const double Chrome = 190; // search bar + separator + path banner + the window's own margins

    [TestMethod]
    public void ComputeRowBudget_RoomForFewerRowsThanTheRange_ShrinksToWhatFits() =>
        Assert.AreEqual(7, InlineCardMetrics.ComputeRowBudget(Chrome + (RowHeight * 7), Chrome, RowHeight));

    [TestMethod]
    public void ComputeRowBudget_RoomForMoreRowsThanTheRange_StopsAtNine()
    {
        Assert.AreEqual(InlineCardMetrics.DefaultRows, InlineCardMetrics.ComputeRowBudget(2000, Chrome, RowHeight));
        Assert.AreEqual(InlineCardMetrics.DefaultRows, InlineCardMetrics.ComputeRowBudget(Chrome + (RowHeight * 9), Chrome, RowHeight));
    }

    [TestMethod]
    public void ComputeRowBudget_NoRoomAtAll_StillShowsTheFloor()
    {
        // The floor the card is allowed to exceed its space for. It sits at two rows on purpose: a card that
        // overruns the window it is docked to is the one thing this layout is meant to stop doing, and two
        // rows still show a selection with a neighbour. The list scrolls, so nothing becomes unreachable.
        Assert.AreEqual(InlineCardMetrics.MinRows, InlineCardMetrics.ComputeRowBudget(120, Chrome, RowHeight));
        Assert.AreEqual(InlineCardMetrics.MinRows, InlineCardMetrics.ComputeRowBudget(0, Chrome, RowHeight));
        Assert.AreEqual(InlineCardMetrics.MinRows, InlineCardMetrics.ComputeRowBudget(-500, Chrome, RowHeight));
    }

    [TestMethod]
    public void ComputeRowBudget_RoomForTwoRows_TakesTheFloorWithoutPaddingItUp()
    {
        Assert.AreEqual(2, InlineCardMetrics.ComputeRowBudget(Chrome + (RowHeight * 2), Chrome, RowHeight));
        // One pixel short of the second row and the floor still holds the card at two rather than letting it
        // collapse -- which is the whole reason MinRows and DefaultRows are both parameters.
        Assert.AreEqual(2, InlineCardMetrics.ComputeRowBudget((Chrome + (RowHeight * 2)) - 1, Chrome, RowHeight));
    }

    [TestMethod]
    public void CanAffordPathReserve_KeepsTheEstimateWhileTheFloorStillFits()
    {
        // The reserve is five lines of mostly empty shell, so it survives only while the floor rows fit
        // beside it. Here there is room for more than that.
        Assert.IsTrue(InlineCardMetrics.CanAffordPathReserve(Chrome + (RowHeight * 5), Chrome, RowHeight));
        Assert.IsTrue(InlineCardMetrics.CanAffordPathReserve(Chrome + (RowHeight * InlineCardMetrics.MinRows), Chrome, RowHeight));
    }

    [TestMethod]
    public void CanAffordPathReserve_DroppedWhenTheEstimateIsWhatIsInTheWay()
    {
        Assert.IsFalse(InlineCardMetrics.CanAffordPathReserve((Chrome + (RowHeight * InlineCardMetrics.MinRows)) - 1, Chrome, RowHeight));
        Assert.IsFalse(InlineCardMetrics.CanAffordPathReserve(Chrome, Chrome, RowHeight));
        Assert.IsFalse(InlineCardMetrics.CanAffordPathReserve(0, Chrome, RowHeight));
    }

    [TestMethod]
    public void ComputeRowBudget_ExactRoomForNRows_IsNRows()
    {
        // No off-by-one at the boundary: the row whose height exactly fits is included.
        Assert.AreEqual(5, InlineCardMetrics.ComputeRowBudget(Chrome + (RowHeight * 5), Chrome, RowHeight));
        Assert.AreEqual(4, InlineCardMetrics.ComputeRowBudget((Chrome + (RowHeight * 5)) - 1, Chrome, RowHeight));
    }

    [TestMethod]
    public void ComputeRowBudget_ImpossibleRowHeight_FallsBackToTheFullRange() =>
        // A degenerate row height must not divide by zero; the card keeps the budget it always had.
        Assert.AreEqual(InlineCardMetrics.DefaultRows, InlineCardMetrics.ComputeRowBudget(500, Chrome, 0));

    [TestMethod]
    public void AvailableCardHeight_RoomBelowTheWindow_IsUsedWithoutCoveringIt() =>
        // Working area 1080, a 600-tall window with 500 DIP of screen left below it: the card takes that 500
        // rather than a share of the window, because it can sit under the window and cover nothing at all.
        Assert.AreEqual(500.0, InlineCardMetrics.AvailableCardHeight(1080, 600, 500), 0.001);

    [TestMethod]
    public void AvailableCardHeight_NoRoomBelow_CapsToAShareOfTheWindow() =>
        // The same window at the bottom of a 768-tall screen: 10 DIP below it, so the card has to sit over
        // the window, and 60% of the window (360) is what it may take -- not 90% of the screen.
        Assert.AreEqual(360.0, InlineCardMetrics.AvailableCardHeight(768, 600, 10), 0.001);

    [TestMethod]
    public void AvailableCardHeight_WindowTallerThanTheScreen_IsCappedByTheWorkingArea() =>
        // A maximized dialog on a 768-tall screen: 60% of the window would exceed the screen's own share.
        Assert.AreEqual(768 * InlineCardMetrics.WorkingAreaHeightShare, InlineCardMetrics.AvailableCardHeight(768, 10000, 0), 0.001);

    [TestMethod]
    public void AvailableCardHeight_NoAnchoredWindow_UsesTheWorkingAreaShare() =>
        // The desktop, or no tracked window: there is nothing to share the screen with.
        Assert.AreEqual(1080 * InlineCardMetrics.WorkingAreaHeightShare, InlineCardMetrics.AvailableCardHeight(1080, 0, 0), 0.001);
}
