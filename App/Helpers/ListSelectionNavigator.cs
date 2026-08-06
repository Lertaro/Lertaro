namespace Lertaro.App.Helpers;

/// <summary>
/// Picks the next selectable row when an arrow key moves through a result or action list, skipping the
/// rows that exist only to be looked at -- section headers, separators, the "no results" placeholder,
/// disabled entries.
/// </summary>
/// <remarks>
/// Shared by every list that navigates this way rather than being written out per call site. The four
/// copies this replaces had been carrying the same defect since they were copied from one another: they
/// stopped when the walk came back to the index it started from, which a modular step can never reach
/// when that index is -1. A list holding nothing selectable -- only headers, or only the "no results"
/// row -- then spun on the UI thread forever, pegging a core and freezing the window.
///
/// -1 is not an edge case here, it is the normal state for a moment: WPF resets SelectedIndex whenever
/// the items are replaced, and the results list is replaced on every keystroke while a search streams
/// in. The callback that puts the selection back runs at Render priority, so an arrow key pressed in
/// that gap arrives with nothing selected -- which is why the hang only ever showed up while typing,
/// and never reproducibly.
/// </remarks>
internal static class ListSelectionNavigator
{
    /// <summary>
    /// The index to select next, or -1 to leave the selection alone (nothing else is selectable).
    /// </summary>
    /// <param name="currentIndex">Where the selection is now; -1 (or out of range) when there is none.</param>
    /// <param name="direction">+1 to move down, -1 to move up. Wraps around either end.</param>
    /// <param name="count">How many rows the list holds.</param>
    /// <param name="isSelectable">Whether the row at an index can hold the selection.</param>
    public static int NextSelectable(int currentIndex, int direction, int count, Func<int, bool> isSelectable)
    {
        if (count <= 0 || direction == 0)
            return -1;

        // With nothing selected, start just outside the list so the first step lands on the first row
        // going down and the last row going up.
        var index = currentIndex >= 0 && currentIndex < count
            ? currentIndex
            : (direction > 0 ? -1 : 0);

        // Bounded by the row count, so this terminates whatever the starting index was. Coming back to
        // where the walk began still stops it early and leaves the selection untouched, which is what
        // keeps a single selectable row from being re-selected (and re-scrolled) on every key press.
        for (var step = 0; step < count; step++)
        {
            index = (index + direction + count) % count;
            if (index == currentIndex)
                break;
            if (isSelectable(index))
                return index;
        }

        return -1;
    }
}
