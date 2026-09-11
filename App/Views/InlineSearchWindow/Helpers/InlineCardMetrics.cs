using Lertaro.App.Services;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// The inline card's geometry. The card shows up to a fixed number of list rows while a search runs, and
// trims to what actually came back once it is not.
//
// Split from the window and the layout manager so the arithmetic is testable without a visual tree.
internal static class InlineCardMetrics
{
    // How many rows the result list shows. 9 matches the Ctrl+1..9 jump range while keeping the complete
    // card bounded: section titles are rows in the list and cannot make a tenth row appear.
    internal const int DefaultRows = 9;

    // The shell reserves this many wrapped path lines so selecting ordinary long paths does not move the
    // bottom-anchored search bar. This is only an estimate for the shell; the path banner itself remains
    // naturally sized and can grow beyond it when the complete path needs more lines.
    internal const int PathPreviewReservedRows = 5;

    /// <summary>What the results area should occupy right now.</summary>
    /// <param name="ShownItems">Bound items to occupy with real rows, including any section titles.</param>
    /// <param name="AreaRows">Rows reserved by the result area while a search is running.</param>
    internal readonly record struct CardLayout(int ShownItems, int AreaRows);

    /// <summary>
    /// Works out what the results area should show, from the shape of the item list and whether a search is
    /// still running.
    /// </summary>
    /// <remarks>
    /// <paramref name="isHeader"/> must be in display order and describe the bound items. The walk stops
    /// after <paramref name="resultBudget"/> LIST ROWS, and keeps everything before the last included result
    /// so a trailing title is not shown without a result beneath it.
    ///
    /// While searching the area stays at the full list-row budget for sizing stability; once settled it
    /// shrinks to exactly what is there. No synthetic rows are rendered for the reserved space.
    /// </remarks>
    internal static CardLayout ComputeLayout(IReadOnlyList<bool> isHeader, bool isSearching, int resultBudget = DefaultRows)
    {
        var budget = Math.Max(0, resultBudget);

        var scanLimit = Math.Min(budget, isHeader.Count);
        var lastIncludedResult = -1;
        for (var i = 0; i < scanLimit; i++)
        {
            if (!isHeader[i]) lastIncludedResult = i;
        }

        // A category heading is useful only when a result follows it. Dropping trailing headings also
        // means the settled card cannot retain an empty category at the bottom of the bounded list.
        var shownItems = lastIncludedResult + 1;

        // While searching, the area keeps the full budget for sizing stability. Once settled, it is exactly
        // the bounded list contents.
        var areaRows = isSearching ? budget : shownItems;
        return new CardLayout(shownItems, areaRows);
    }

    /// <summary>The pixel height of the row area for <paramref name="rows"/> rows.</summary>
    internal static double ResultsAreaHeight(int rows) => Math.Max(0, rows) * UiMetrics.InlineRowHeight;
}
