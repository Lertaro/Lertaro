using Lertaro.App.Services;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// The inline card's geometry. The card shows up to the configured number of result rows while a search runs,
// and trims to what actually came back once it is not.
//
// Split from the window and the layout manager so the arithmetic is testable without a visual tree.
internal static class InlineCardMetrics
{
    // How many result rows the card shows when the user has not chosen a count. A section title is not one of
    // them: it takes a row of the card's height but no slot of the count, so a card set to ten rows can show
    // ten results and the titles that group them.
    internal const int DefaultRows = UiMetrics.DefaultInlineCardRows;

    // The Ctrl jump shortcuts reach 1..9 and then 0, so ten slots. This is the key space, not the card's
    // ceiling -- UiMetrics.MaxInlineCardRows is the largest count the settings box accepts. The hook process
    // maps the same ten keys in KeyboardUtils.MaxJumpSlots.
    internal const int MaxJumpSlots = 10;

    // How many section titles the tallest card is priced with. Titles are free in the user's count but not in
    // height, and the placement decides which corner to hang from against a CONSTANT height
    // (InlineCardSizingSupport.FullCardHeight) because the row count follows what the search returned: measure
    // the card as it is now and it picks a different corner between two result counts, which is the card
    // jumping while the user types. Two is the shape the inline list is built from -- "Current Folder" over
    // "Global Search". A rarer third title (a plugin's own command group, the empty state's opened-folders and
    // recent headers) makes the card one row taller than the room the placement asked for, and the on-screen
    // clamp keeps it reachable.
    internal const int MaxSectionTitleRows = 2;

    // The floor a screen-aware budget never drops below, and it is deliberately low: an over-tall card is
    // the one thing that actually blocks the window it is docked to, so in a space too short for the rows the
    // user asked for the card takes two and scrolls rather than pushing further over the host window. Two rows
    // still show a selection with a neighbour, which is the least the list can be and stay legible.
    internal const int MinRows = 2;

    // How much of the monitor's working area, and how much of the window the card is anchored to, the card
    // may occupy. The second one is what keeps a docked card from covering the dialog it belongs to: it is
    // capped to a share of that window instead of growing to whatever its rows would need.
    internal const double WorkingAreaHeightShare = 0.9;
    internal const double AnchoredWindowHeightShare = 0.6;

    // The docked card's width, in shares of the window it is anchored to and of that window's screen. The
    // user names a percent of the window; the floor keeps the card wide enough to read a path in, the
    // ceiling keeps it from lying across more of somebody else's window than the window itself. The default
    // takes a fifth of the screen over a narrow window because nothing was chosen, so the card is at least
    // as wide as it has always been on the screens people actually use.
    internal const double MinCardWidthShareOfHost = 0.2;
    internal const double MinCardWidthShareOfScreen = 1.0 / 6;
    internal const double DefaultCardWidthShareOfHost = 0.4;
    internal const double DefaultCardWidthShareOfScreen = 0.2;
    internal const double MaxCardWidthShareOfScreen = 0.9;

    /// <summary>What the results area should occupy right now.</summary>
    /// <param name="ShownItems">Bound items to occupy with real rows, including any section titles.</param>
    /// <param name="AreaRows">Rows reserved by the result area while a search is running.</param>
    internal readonly record struct CardLayout(int ShownItems, int AreaRows);

    /// <summary>
    /// Works out what the results area should show, from the shape of the item list and whether a search is
    /// still running.
    /// </summary>
    /// <remarks>
    /// <paramref name="isHeader"/> must be in display order and describe the bound items. The walk stops after
    /// <paramref name="resultBudget"/> RESULTS and keeps everything before the last one of them, so a section
    /// title takes height without costing the user a slot, and a trailing title is not shown with no result
    /// beneath it. <paramref name="listRowBudget"/> is the ceiling on TOTAL rows -- titles included -- which is
    /// what keeps a card whose list carries more titles than the screen has room for inside that room.
    ///
    /// While searching the area keeps the full budget plus the titles in it, never more rows than
    /// <paramref name="listRowBudget"/> allows, for sizing stability; once settled it shrinks to exactly what is
    /// there. No synthetic rows are rendered for the reserved space.
    /// </remarks>
    internal static CardLayout ComputeLayout(
        IReadOnlyList<bool> isHeader,
        bool isSearching,
        int resultBudget = DefaultRows,
        int listRowBudget = int.MaxValue)
    {
        var budget = Math.Max(0, resultBudget);
        var scanLimit = Math.Min(Math.Max(0, listRowBudget), isHeader.Count);

        var resultsKept = 0;
        var lastIncludedResult = -1;
        for (var i = 0; i < scanLimit; i++)
        {
            if (isHeader[i]) continue;
            if (resultsKept == budget) break;
            resultsKept++;
            lastIncludedResult = i;
        }

        // A category heading is useful only when a result follows it. Dropping trailing headings also
        // means the settled card cannot retain an empty category at the bottom of the bounded list.
        var shownItems = lastIncludedResult + 1;

        // While searching, the area keeps the full budget, titles and all, without ever asking for more rows
        // than the room the card was given. Once settled, it is exactly the bounded list contents.
        var areaRows = isSearching
            ? Math.Min(listRowBudget, budget + CountHeaders(isHeader, scanLimit))
            : shownItems;
        return new CardLayout(shownItems, areaRows);
    }

    private static int CountHeaders(IReadOnlyList<bool> isHeader, int limit)
    {
        var count = 0;
        for (var i = 0; i < limit; i++)
        {
            if (isHeader[i]) count++;
        }

        return count;
    }

    /// <summary>The pixel height of the row area for <paramref name="rows"/> rows.</summary>
    internal static double ResultsAreaHeight(int rows) => Math.Max(0, rows) * UiMetrics.InlineRowHeight;

    /// <summary>The height of the card for the rows it draws, plus the chrome the caller measured.</summary>
    /// <remarks>
    /// Deliberately takes no row budget. The budget is a ceiling on what the list may show and on the room the
    /// card may use, never a floor to reserve: charging it is what left the empty-state card (two section
    /// headers and two rows against a four-result budget) standing at the full row budget with a blank band
    /// under the list until a real search filled the card. Rows are charged only while the list is actually
    /// drawn -- a card with nothing on screen is a search bar, whatever a stale visibility binding said a pass
    /// earlier.
    /// </remarks>
    internal static double CardHeight(int areaRows, bool listDrawn, double chrome) =>
        ResultsAreaHeight(listDrawn && areaRows > 0 ? areaRows : 0) + chrome;

    /// <summary>Whether a sizing pass may run while a search is still streaming results in.</summary>
    /// <remarks>
    /// Rows that are still arriving are not a size: a pass driven by them walks the card up and down the screen,
    /// which is why a streaming search normally waits for the settled pass. Once the area is frozen and the list
    /// is actually drawn, the height is a function of the row budget, the chrome and the banner alone, so a pass
    /// driven by a new host, a monitor change or the banner appearing has to apply -- one that is vetoed instead
    /// pins the card to whatever the previous host settled at, and a search that never settles keeps it there.
    /// </remarks>
    internal static bool CanApplyWhileSearching(bool searching, bool forSummon, int? frozenAreaRows, bool listDrawn) =>
        !searching || forSummon || (frozenAreaRows is > 0 && listDrawn);

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
    /// is the preferred answer and wins when that space can hold the full card, because it covers none of the
    /// window the user is working in (the taskbar strip counts as space, and is allowed to be covered);
    /// anything shorter goes over the anchored window, where it may take a share of that window's height
    /// rather than all of it. A zero <paramref name="activeWindowHeight"/> means there is no window to be
    /// anchored to (the desktop, or nothing tracked), so only the working-area share applies.
    /// </remarks>
    internal static double AvailableCardHeight(
        double workingAreaHeight,
        double activeWindowHeight,
        double spaceBelowActiveWindow,
        double spaceBelowAnchorTop,
        double fullCardHeight)
    {
        var workingAreaLimit = Math.Max(0, workingAreaHeight) * WorkingAreaHeightShare;
        if (activeWindowHeight <= 0)
            return workingAreaLimit;

        // When the card has to lie over the window it is anchored to the top of that placement is fixed --
        // it starts at the anchor's top edge -- so the room left on screen below *that edge* is the real
        // ceiling, alongside the share of the window. Capping by the window share alone left the positioner's
        // on-screen clamp pulling the whole card up to a y that no longer moves with the window, which is how
        // a dialog near the bottom of the screen ended up with the card parked at a fixed point above it.
        var overAnchoredWindow = Math.Min(activeWindowHeight * AnchoredWindowHeightShare, spaceBelowAnchorTop);
        var room = HasRoomToHangBelow(spaceBelowActiveWindow, fullCardHeight)
            ? spaceBelowActiveWindow
            : overAnchoredWindow;
        return Math.Min(workingAreaLimit, room);
    }

    /// <summary>
    /// The card's row ceiling as the count the user set for the context it is docked in, held to the range the
    /// settings box takes. A ceiling, not a promise -- <see cref="ComputeRowBudget"/> still decides how many of
    /// those rows plus their titles the space the card has can hold.
    /// </summary>
    /// <remarks>
    /// user-settings.json is hand-editable, so a stored number outside the box reads as its nearest edge.
    /// </remarks>
    internal static int ResolveRowCap(int configuredRows) =>
        Math.Clamp(configuredRows, UiMetrics.MinInlineCardRows, UiMetrics.MaxInlineCardRows);

    /// <summary>
    /// The key a jump slot is labeled with: slots 1..9 keep their number, and the tenth is "0" -- the key the
    /// hook process maps back to slot ten.
    /// </summary>
    internal static string JumpSlotLabel(int slot) => slot == MaxJumpSlots ? "0" : slot.ToString();

    /// <summary>
    /// How many list rows fit in <paramref name="availableHeight"/> once the card's non-row height is paid
    /// for, bounded by <paramref name="maxRows"/> -- the user's count plus the titles it rides on -- and never
    /// below <paramref name="minRows"/>.
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

    /// <summary>
    /// The docked card's width in DIP: the user's percent of <paramref name="hostWidth"/>, held between the
    /// floor and the ceiling named above.
    /// </summary>
    /// <remarks>
    /// A null <paramref name="percent"/> is the default, not a missing number. <paramref name="screenWidth"/>
    /// is the anchored window's own screen; 0 means nothing could be read for it, and then the shares of the
    /// host carry the bounds alone -- a screen the card is not on has no claim on its width.
    /// </remarks>
    internal static double ResolveDockedWidth(double hostWidth, double screenWidth, int? percent)
    {
        var hasScreen = screenWidth > 0;

        var floor = hostWidth * MinCardWidthShareOfHost;
        if (hasScreen) floor = Math.Max(floor, screenWidth * MinCardWidthShareOfScreen);
        var ceiling = hasScreen ? Math.Min(hostWidth, screenWidth * MaxCardWidthShareOfScreen) : hostWidth;

        // A window narrower than the screen's own floor can only be answered by giving the floor up: a card
        // wider than the window it belongs to is the exact thing this setting exists to prevent.
        if (floor > ceiling) floor = ceiling;

        var desired = percent is null
            ? hasScreen
                ? Math.Max(hostWidth * DefaultCardWidthShareOfHost, screenWidth * DefaultCardWidthShareOfScreen)
                : hostWidth * DefaultCardWidthShareOfHost
            : hostWidth * Math.Clamp(percent.Value, UiMetrics.MinInlineCardWidthPercent, UiMetrics.MaxInlineCardWidthPercent) / 100.0;

        return Math.Clamp(desired, floor, ceiling);
    }
}
