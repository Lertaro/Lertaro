using Lertaro.App.Services;
using Lertaro.App.Views.InlineSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

// The inline card's geometry. The card shows up to a fixed number of RESULT rows -- 9, matching the
// Ctrl+1..9 jump range -- and trims to what actually came back once a search is not running.
//
// The point of the layout arithmetic is that section titles must not eat that budget. With "Current Folder"
// and "Global Search" both showing, a budget that counted the titles left only 7 results visible and made
// Ctrl+8/9 point below the fold; the number of reachable results would depend on which titles happened to
// be present.
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
    public void ComputeLayout_WithHeaders_AreaIsTheBudgetPlusTheTitles()
    {
        // The reported case: a "Current Folder" title, its results, then a "Global Search" title and more.
        var items = Items(false, false, false, true, false, false);
        var layout = InlineCardMetrics.ComputeLayout(items, isSearching: true);

        // 9 result slots plus the one title actually showing = 10 rows. The title is EXTRA: it did not take
        // a slot away from the results, which is the whole point.
        Assert.AreEqual(10, layout.AreaRows);
        Assert.AreEqual(6, layout.ShownItems, "all five results plus their title");
        Assert.AreEqual(4, layout.UnfilledSlots, "the results still on their way");
    }

    [TestMethod]
    public void ComputeLayout_HeadersDoNotReduceHowManyResultsFit()
    {
        // Nine results with no titles, versus nine results split across two titles. Either way all nine
        // results are visible, which is what keeps Ctrl+9 reachable; previously the split case could only
        // fit seven, and which of the two you got depended on whether the titles happened to be present.
        var plain = new bool[9];
        var plainLayout = InlineCardMetrics.ComputeLayout(plain, isSearching: true);

        var withHeaders = new bool[11];
        withHeaders[0] = true;   // "Current Folder"
        withHeaders[5] = true;   // "Global Search"
        var splitLayout = InlineCardMetrics.ComputeLayout(withHeaders, isSearching: true);

        // Both show nine results; the split case is two rows taller because of its two titles, never two
        // results shorter.
        Assert.AreEqual(9, plainLayout.ShownItems);
        Assert.AreEqual(11, splitLayout.ShownItems, "nine results plus both titles");
        Assert.AreEqual(9, plainLayout.AreaRows);
        Assert.AreEqual(11, splitLayout.AreaRows, "the titles are added to the 9, not taken out of it");
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
    public void ComputeLayout_WhileSearching_IsNeverTallerThanTheBudgetPlusTheTitles()
    {
        // Guards the property that keeps a streaming search from resizing the card: the area depends on the
        // budget and how many titles are showing, never on how many results have arrived.
        for (var results = 0; results <= 30; results++)
        {
            var items = new bool[results + 2];
            items[0] = true;
            items[1] = true;
            var layout = InlineCardMetrics.ComputeLayout(items, isSearching: true);

            Assert.IsLessThanOrEqualTo(InlineCardMetrics.DefaultRows + 2, layout.AreaRows);
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
