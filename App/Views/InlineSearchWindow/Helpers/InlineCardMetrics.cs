using Lertaro.App.Services;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// The inline card's geometry. The card shows up to a fixed number of RESULT rows while a search runs, and
// trims to what actually came back once it is not.
//
// Split from the window and the layout manager so the arithmetic is testable without a visual tree.
internal static class InlineCardMetrics
{
    // How many RESULT rows the card shows. 9 matches the Ctrl+1..9 jump range exactly, so every slot the
    // shortcut can reach is one the user can see.
    //
    // Section titles ("Current Folder", "Global Search") are deliberately NOT part of this budget: they
    // ride on top of it. Counting them would mean only 7 results were visible whenever both titles showed
    // and 9 when neither did -- the number of reachable results would depend on which titles happened to be
    // present, and Ctrl+8/9 would point at rows below the fold.
    internal const int DefaultRows = 9;

    /// <summary>What the results area should occupy right now.</summary>
    /// <param name="ShownItems">Bound items to occupy with real rows, i.e. the results that fit plus the titles above them.</param>
    /// <param name="AreaRows">Total rows the area is sized to, including any placeholder rows.</param>
    internal readonly record struct CardLayout(int ShownItems, int AreaRows)
    {
        /// <summary>Rows still waiting on a search to fill them.</summary>
        internal int UnfilledSlots => Math.Max(0, AreaRows - ShownItems);
    }

    /// <summary>
    /// Works out what the results area should show, from the shape of the item list and whether a search is
    /// still running.
    /// </summary>
    /// <remarks>
    /// <paramref name="isHeader"/> must be in display order and describe the bound items. The walk stops
    /// after <paramref name="resultBudget"/> RESULTS (titles do not count), and keeps everything before that
    /// point -- so the titles belonging to those results come along and any title after them does not (it
    /// would be a heading with nothing under it).
    ///
    /// While searching the area stays at the full budget plus however many titles are showing, with the
    /// remainder as placeholder rows; once settled it shrinks to exactly what is there. Neither ever grows
    /// with the number of results, so a streaming search does not resize the card.
    /// </remarks>
    internal static CardLayout ComputeLayout(IReadOnlyList<bool> isHeader, bool isSearching, int resultBudget = DefaultRows)
    {
        var budget = Math.Max(0, resultBudget);

        var totalResults = 0;
        foreach (var header in isHeader)
        {
            if (!header) totalResults++;
        }

        // While searching the full budget is kept (placeholders hold the rest); settled, only what came back.
        var resultsToShow = isSearching ? budget : Math.Min(budget, totalResults);

        // Walk to the last result that fits, remembering where it was. Counting a title as it is passed
        // would also count a trailing title whose own results are all beyond the budget -- a heading drawn
        // with nothing under it. Everything up to the last included RESULT is what shows, which brings
        // along the titles above it and drops any after it.
        var shownResults = 0;
        var lastIncludedIndex = -1;
        for (var i = 0; i < isHeader.Count && shownResults < resultsToShow; i++)
        {
            if (isHeader[i]) continue;
            shownResults++;
            lastIncludedIndex = i;
        }

        var shownItems = lastIncludedIndex + 1;

        // Unfilled slots are only ever for results still on their way, so while searching the area is the
        // rows being shown plus a placeholder for each result the walk did not reach. Once settled it is
        // exactly the rows being shown.
        var areaRows = isSearching ? shownItems + Math.Max(0, budget - shownResults) : shownItems;
        return new CardLayout(shownItems, areaRows);
    }

    /// <summary>The pixel height of the row area for <paramref name="rows"/> rows.</summary>
    internal static double ResultsAreaHeight(int rows) => Math.Max(0, rows) * UiMetrics.InlineRowHeight;
}
