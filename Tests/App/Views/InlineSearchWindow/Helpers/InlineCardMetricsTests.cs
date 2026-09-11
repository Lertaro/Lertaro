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
        Assert.AreEqual(6, layout.UnfilledSlots);
    }

    [TestMethod]
    public void ComputeLayout_WithHeaders_AreaStaysWithinTheBudget()
    {
        // The reported case: a "Current Folder" title, its results, then a "Global Search" title and more.
        var items = Items(false, false, false, true, false, false);
        var layout = InlineCardMetrics.ComputeLayout(items, isSearching: true);

        Assert.AreEqual(9, layout.AreaRows);
        Assert.AreEqual(6, layout.ShownItems, "all five results plus their title");
        Assert.AreEqual(3, layout.UnfilledSlots, "the remaining list slots are placeholders");
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
        Assert.AreEqual(0, layout.UnfilledSlots, "a settled search has nothing left to wait for");
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
            Assert.IsGreaterThanOrEqualTo(0, layout.UnfilledSlots);
            Assert.AreEqual(layout.AreaRows, layout.ShownItems + layout.UnfilledSlots,
                "the bound rows and the placeholders together are exactly the area");
        }
    }

    [TestMethod]
    public void ResultsAreaHeight_IsRowsTimesRowHeight()
    {
        Assert.AreEqual(RowHeight * 9, InlineCardMetrics.ResultsAreaHeight(9));
        Assert.AreEqual(0, InlineCardMetrics.ResultsAreaHeight(0));
        Assert.AreEqual(0, InlineCardMetrics.ResultsAreaHeight(-3));
    }
}
