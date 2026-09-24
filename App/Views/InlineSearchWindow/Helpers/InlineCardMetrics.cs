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

    // The cap over a file dialog, where the card hangs off the dialog's own button row: everything under
    // that row belongs to somebody else's window, so four rows is what the card takes before it is covering
    // the taskbar rather than the dialog. The jump shortcuts keep working 1..4; 5..9 simply have nothing to
    // jump to here.
    internal const int DialogRows = 4;

    // The floor a screen-aware budget never drops below, and it is deliberately low: an over-tall card is
    // the one thing that actually blocks the window it is docked to, so in a space too short for four rows
    // the card takes two and scrolls rather than pushing further over the host window. Two rows still show a
    // selection with a neighbour, which is the least the list can be and stay legible.
    internal const int MinRows = 2;

    // How much of the monitor's working area, and how much of the window the card is anchored to, the card
    // may occupy. The second one is what keeps a docked card from covering the dialog it belongs to: it is
    // capped to a share of that window instead of growing to whatever its rows would need.
    internal const double WorkingAreaHeightShare = 0.9;
    internal const double AnchoredWindowHeightShare = 0.6;

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

    /// <summary>
    /// Whether the space under an anchored window can hold the tallest the card can ever be. The one place
    /// that answers it, because the height budget and the placement have to agree: a card sized to hang
    /// outside and then drawn inside (or the reverse) is what made it cover a file list it had room to sit
    /// below, and asking about the card's CURRENT height made it change corners between two result counts.
    /// </summary>
    internal static bool HasRoomToHangBelow(double spaceBelowActiveWindow, double fullCardHeight) =>
        spaceBelowActiveWindow >= fullCardHeight;

    /// <summary>
    /// How much vertical room the card has, in DIP, given the screen and the window it is anchored to.
    /// </summary>
    /// <remarks>
    /// Deliberately independent of the card's own height: deriving a budget from a size that the budget
    /// itself decides is what makes a layout oscillate between two answers. Hanging BELOW the anchored window
    /// is the preferred answer and wins when that space can hold the full card, because it covers nothing;
    /// anything shorter goes over the anchored window, where it may take a share of that window's height
    /// rather than all of it. A zero <paramref name="activeWindowHeight"/> means there is no window to be
    /// anchored to (the desktop, or nothing tracked), so only the working-area share applies.
    /// </remarks>
    internal static double AvailableCardHeight(
        double workingAreaHeight,
        double activeWindowHeight,
        double spaceBelowActiveWindow,
        double fullCardHeight)
    {
        var workingAreaLimit = Math.Max(0, workingAreaHeight) * WorkingAreaHeightShare;
        if (activeWindowHeight <= 0)
            return workingAreaLimit;

        var overAnchoredWindow = activeWindowHeight * AnchoredWindowHeightShare;
        var room = HasRoomToHangBelow(spaceBelowActiveWindow, fullCardHeight)
            ? spaceBelowActiveWindow
            : overAnchoredWindow;
        return Math.Min(workingAreaLimit, room);
    }

    /// <summary>
    /// How many list rows fit in <paramref name="availableHeight"/> once the card's non-row height is paid
    /// for, bounded by the Ctrl+1..9 budget and never below <paramref name="minRows"/>.
    /// </summary>
    /// <remarks>
    /// When not even <paramref name="minRows"/> fit, the minimum wins: the card then takes more of the screen
    /// than the space allowed, because a scrollable card is more useful than one squeezed to a row or two.
    /// </remarks>
    internal static int ComputeRowBudget(
        double availableHeight,
        double chromeHeight,
        double rowHeight,
        int minRows = MinRows,
        int maxRows = DefaultRows)
    {
        if (rowHeight <= 0) return maxRows;

        var rows = (int)Math.Floor((availableHeight - chromeHeight) / rowHeight);
        return Math.Clamp(rows, Math.Min(minRows, maxRows), maxRows);
    }
}
