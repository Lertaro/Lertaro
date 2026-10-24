using Lertaro.App.Services;
using Lertaro.App.Views.InlineSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

// The inline card's geometry. The card shows up to the configured number of RESULT rows -- ten by default, the
// same count the Ctrl jump shortcuts reach -- and trims to what actually came back once a search is not running.
//
// The point of the layout arithmetic is that a section title takes height but none of the user's rows: the count
// they set is how many results show, whatever the list's headers cost on top of it. A header is kept only while
// a result follows it.
[TestClass]
public sealed class InlineCardMetricsTests
{
    private static double RowHeight => UiMetrics.InlineRowHeight;

    // The ceiling an untouched card uses, so these cases read the same way the card does.
    private static int Budget => InlineCardMetrics.DefaultRows;

    // isHeader describes the bound items in display order: true for a section title, false for a result.
    private static bool[] Items(params bool[] isHeader) => isHeader;

    [TestMethod]
    public void ComputeLayout_NoHeaders_ShowsTheWholeBudget()
    {
        var layout = InlineCardMetrics.ComputeLayout(Items(false, false, false), isSearching: true);

        Assert.AreEqual(3, layout.ShownItems);
        Assert.AreEqual(Budget, layout.AreaRows, "the area keeps the full result budget while searching");
    }

    [TestMethod]
    public void ComputeLayout_HeadersTakeHeightButNotASlotOfTheBudget()
    {
        // The reported case: a "Current Folder" title, its results, then a "Global Search" title and more.
        var items = Items(false, false, false, true, false, false);
        var layout = InlineCardMetrics.ComputeLayout(items, isSearching: true);

        Assert.AreEqual(6, layout.ShownItems, "all five results plus their title");
        Assert.AreEqual(Budget + 1, layout.AreaRows, "the count reserved plus the one title it rides on");
    }

    [TestMethod]
    public void ComputeLayout_HeadersDoNotCostResults()
    {
        // A full count of plain rows versus the same count split under two titles. Both show ten results, and
        // the second card is two rows taller -- which is what "a title spends no slot of the count" means.
        var plain = new bool[Budget];
        var plainLayout = InlineCardMetrics.ComputeLayout(plain, isSearching: false);

        var withHeaders = new bool[Budget + 2];
        withHeaders[0] = true;   // "Current Folder"
        withHeaders[5] = true;   // "Global Search"
        var splitLayout = InlineCardMetrics.ComputeLayout(withHeaders, isSearching: false);

        Assert.AreEqual(Budget, plainLayout.ShownItems);
        Assert.AreEqual(Budget + 2, splitLayout.ShownItems, "ten results plus the two titles that group them");
    }

    [TestMethod]
    public void ComputeLayout_SearchingNeverReservesMoreThanTheRowBudget()
    {
        // Four titles in a room priced for two: the reservation stops at the room, rather than asking the card
        // to be taller than the space the placement gave it.
        var items = Items(true, true, true, true, false, false);
        var layout = InlineCardMetrics.ComputeLayout(items, isSearching: true, resultBudget: 10, listRowBudget: 12);

        Assert.AreEqual(12, layout.AreaRows);
    }

    [TestMethod]
    public void ComputeLayout_ListRowBudget_CutsResultsBeforeOverrunningTheRoom()
    {
        // The screen has room for six rows and the list carries two titles: four results show rather than the
        // ten asked for, because the room the card was given is the harder bound and the list scrolls.
        var items = new bool[20];
        items[0] = true;
        items[3] = true;

        var layout = InlineCardMetrics.ComputeLayout(items, isSearching: false, resultBudget: 10, listRowBudget: 6);

        Assert.AreEqual(6, layout.ShownItems, "two titles and the four results that fit under them");
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
    public void CardHeight_SettledShortList_ChargesTheRowsItDrawsNotTheRowBudget()
    {
        // Empty state over a file dialog: two section headers and two folders, against a four-result budget
        // with room for six rows. The card is four rows tall -- reserving the budget left a blank band under
        // the list until the first real search filled the card.
        var isHeader = new[] { true, false, true, false };
        var layout = InlineCardMetrics.ComputeLayout(isHeader, isSearching: false, resultBudget: 4, listRowBudget: 6);

        Assert.AreEqual(4, layout.AreaRows);
        Assert.AreEqual(
            InlineCardMetrics.ResultsAreaHeight(4) + 33,
            InlineCardMetrics.CardHeight(layout.AreaRows, listDrawn: true, chrome: 33),
            0.001);
    }

    [TestMethod]
    public void CardHeight_ListNotDrawn_IsTheSearchBarAlone()
    {
        // A card whose list is not drawn has no rows to charge, however many the last layout held.
        Assert.AreEqual(33, InlineCardMetrics.CardHeight(6, listDrawn: false, chrome: 33), 0.001);
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

        Assert.AreEqual(Budget, layout.ShownItems);
        Assert.AreEqual(Budget, layout.AreaRows);
    }

    [TestMethod]
    public void ComputeLayout_WhileSearching_HoldsOneHeightWhileResultsArrive()
    {
        // Guards the property that keeps a streaming search from resizing the card: the reserved area is the
        // count plus the titles in the list, and it does not move as more of either arrives.
        for (var results = 0; results <= 30; results++)
        {
            var items = new bool[results + 2];
            items[0] = true;
            items[1] = true;
            var layout = InlineCardMetrics.ComputeLayout(items, isSearching: true);

            Assert.AreEqual(Budget + InlineCardMetrics.MaxSectionTitleRows, layout.AreaRows,
                "the two titles are reserved alongside the count, whatever has landed yet");
            Assert.IsLessThanOrEqualTo(Budget + InlineCardMetrics.MaxSectionTitleRows, layout.ShownItems);
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
    public void ComputeRowBudget_RoomForMoreRowsThanTheRange_StopsAtTheCeiling()
    {
        Assert.AreEqual(InlineCardMetrics.DefaultRows, InlineCardMetrics.ComputeRowBudget(2000, Chrome, RowHeight));
        Assert.AreEqual(Budget, InlineCardMetrics.ComputeRowBudget(Chrome + (RowHeight * Budget), Chrome, RowHeight));
    }

    [TestMethod]
    public void ComputeRowBudget_DialogSizedCap_StopsAtFourWhateverTheRoom()
    {
        // What a file-dialog card is configured with, capped by the room the dialog leaves: the ceiling alone
        // says "short card here", because the space under a dialog's button row is mostly empty desktop.
        Assert.AreEqual(UiMetrics.DefaultInlineCardDialogRows,
            InlineCardMetrics.ComputeRowBudget(2000, Chrome, RowHeight, maxRows: UiMetrics.DefaultInlineCardDialogRows));
        Assert.AreEqual(UiMetrics.DefaultInlineCardDialogRows,
            InlineCardMetrics.ComputeRowBudget(Chrome + (RowHeight * 9), Chrome, RowHeight, maxRows: UiMetrics.DefaultInlineCardDialogRows));
        // The floor and the cap stay independent: a space that fits two rows gets two, not four.
        Assert.AreEqual(2,
            InlineCardMetrics.ComputeRowBudget(Chrome + (RowHeight * 2), Chrome, RowHeight, maxRows: UiMetrics.DefaultInlineCardDialogRows));
        Assert.AreEqual(InlineCardMetrics.MinRows,
            InlineCardMetrics.ComputeRowBudget(0, Chrome, RowHeight, maxRows: UiMetrics.DefaultInlineCardDialogRows));
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

    // The row ceiling the settings boxes decide. Numbers outside the box only reach the card through a
    // hand-edited user-settings.json, and then read as the nearest edge.
    [TestMethod]
    public void ResolveRowCap_AConfiguredCountIsTakenAsIs()
    {
        Assert.AreEqual(4, InlineCardMetrics.ResolveRowCap(4));
        Assert.AreEqual(7, InlineCardMetrics.ResolveRowCap(7));
        Assert.AreEqual(UiMetrics.MaxInlineCardRows, InlineCardMetrics.ResolveRowCap(UiMetrics.MaxInlineCardRows));
    }

    [TestMethod]
    public void ResolveRowCap_CountsOutsideTheBoxReadAsTheNearestEdge()
    {
        Assert.AreEqual(UiMetrics.MinInlineCardRows, InlineCardMetrics.ResolveRowCap(1));
        Assert.AreEqual(UiMetrics.MinInlineCardRows, InlineCardMetrics.ResolveRowCap(0));
        Assert.AreEqual(UiMetrics.MaxInlineCardRows, InlineCardMetrics.ResolveRowCap(99));
    }

    [TestMethod]
    public void ResolveRowCap_BothContextDefaultsSurviveIt() =>
        // An ordinary window and a file dialog are separate settings with separate defaults, and both have to
        // read back unchanged: four is the whole of what a dialog card shows by default, ten the window's.
        Assert.AreEqual(UiMetrics.DefaultInlineCardDialogRows, InlineCardMetrics.ResolveRowCap(UiMetrics.DefaultInlineCardDialogRows));

    [TestMethod]
    [DataRow(1, "1")]
    [DataRow(9, "9")]
    public void JumpSlotLabel_TheFirstNineSlotsAreTheirOwnNumbers(int slot, string expected) =>
        Assert.AreEqual(expected, InlineCardMetrics.JumpSlotLabel(slot));

    [TestMethod]
    public void JumpSlotLabel_TheTenthSlotIsZero() =>
        // Ctrl+0, the key the hook process maps back to slot ten.
        Assert.AreEqual("0", InlineCardMetrics.JumpSlotLabel(InlineCardMetrics.MaxJumpSlots));

    [TestMethod]
    public void AvailableCardHeight_RoomBelowTheWindow_IsUsedWithoutCoveringIt() =>
        // Working area 1080, a 600-tall window with 500 DIP of screen left below it: the card takes that 500
        // rather than a share of the window, because it can sit under the window and cover nothing at all.
        Assert.AreEqual(500.0, InlineCardMetrics.AvailableCardHeight(1080, 600, 500, 900, 100), 0.001);

    [TestMethod]
    public void AvailableCardHeight_NoRoomBelow_CapsToAShareOfTheWindow() =>
        // The same window at the bottom of a 768-tall screen: 10 DIP below it, nowhere near the whole card,
        // so the card has to sit over the window -- and 60% of the window (360) is what it may take -- not
        // 90% of the screen.
        Assert.AreEqual(360.0, InlineCardMetrics.AvailableCardHeight(768, 600, 10, 610, 100), 0.001);

    [TestMethod]
    public void AvailableCardHeight_RoomBelowHoldingTheWholeCard_UsesItWithoutCoveringTheWindow()
    {
        // A 607-tall window with room under it for the tallest card there is (495): the card hangs below at
        // that room and covers none of the window.
        Assert.IsTrue(InlineCardMetrics.HasRoomToHangBelow(520, 495));
        Assert.AreEqual(520.0, InlineCardMetrics.AvailableCardHeight(1152, 607, 520, 1000, fullCardHeight: 495), 0.001);
    }

    [TestMethod]
    public void AvailableCardHeight_RoomBelowShorterThanTheWholeCard_GoesOverTheWindow()
    {
        // Measured off a real Explorer window docked to its own file list: it ended 222px above the working
        // area's bottom edge, which is no room for a whole 495px card. So the card goes over the window at
        // 60% of it -- and because the placement asks the same HasRoomToHangBelow question, the two cannot
        // pick opposite answers, which is what used to make the card jump as the row count changed.
        Assert.IsFalse(InlineCardMetrics.HasRoomToHangBelow(222, 495));
        Assert.AreEqual(
            607 * InlineCardMetrics.AnchoredWindowHeightShare,
            InlineCardMetrics.AvailableCardHeight(1152, 607, 222, 600, fullCardHeight: 495),
            0.001);
    }

    [TestMethod]
    public void AvailableCardHeight_WindowTallerThanTheScreen_IsCappedByTheWorkingArea() =>
        // A maximized dialog on a 768-tall screen: 60% of the window would exceed the screen's own share.
        Assert.AreEqual(768 * InlineCardMetrics.WorkingAreaHeightShare, InlineCardMetrics.AvailableCardHeight(768, 10000, 0, 700, 100), 0.001);

    [TestMethod]
    public void AvailableCardHeight_NoAnchoredWindow_UsesTheWorkingAreaShare() =>
        // The desktop, or no tracked window: there is nothing to share the screen with.
        Assert.AreEqual(1080 * InlineCardMetrics.WorkingAreaHeightShare, InlineCardMetrics.AvailableCardHeight(1080, 0, 0, 0, 100), 0.001);

    [TestMethod]
    public void AvailableCardHeight_CappedByTheRoomBelowItsOwnAnchorTop()
    {
        // A 648-tall dialog near the bottom of the screen, so the card has to lie over it: 60% of the window
        // is 388.8, but its top edge is anchored ~200 DIP above the working area's bottom, and the positioner
        // clamps the card to stay on screen. Sized to the window share it would have been pulled upward off its
        // anchor -- which is what showed as a card stuck at a fixed point while the dialog kept moving down.
        Assert.AreEqual(200.0, InlineCardMetrics.AvailableCardHeight(1152, 648, 0, 200, fullCardHeight: 495), 0.001);

        // The share still wins while there is room below the anchor, so nothing changes for the ordinary case.
        Assert.AreEqual(648 * InlineCardMetrics.AnchoredWindowHeightShare,
            InlineCardMetrics.AvailableCardHeight(1152, 648, 0, 400, fullCardHeight: 495), 0.001);
    }

    // The docked width, in DIP, with every number below read as if both the window and its screen were 96 DPI.
    // A fullscreen host on a 1920 screen is the shape that started this: the old fixed two thirds of the
    // window was 1280 of it, which lay across everything the user was trying to look at.
    [TestMethod]
    public void ResolveDockedWidth_DefaultOnAFullscreenWindow_TakesFortyPercent() =>
        Assert.AreEqual(768, InlineCardMetrics.ResolveDockedWidth(1920, 1920, null), 0.001);

    [TestMethod]
    public void ResolveDockedWidth_DefaultOnANarrowWindow_KeepsAFifthOfTheScreen() =>
        // 40% of a 600-wide window is 240, but nothing was chosen, so the card still takes a fifth of the
        // screen -- which fits inside that window, and is what it has always measured on a dialog this size.
        Assert.AreEqual(384, InlineCardMetrics.ResolveDockedWidth(600, 1920, null), 0.001);

    [TestMethod]
    public void ResolveDockedWidth_APercentIsTakenOfTheWindowTheCardDocksTo() =>
        Assert.AreEqual(600, InlineCardMetrics.ResolveDockedWidth(1200, 1920, 50), 0.001);

    [TestMethod]
    public void ResolveDockedWidth_APercentTooSmallIsLiftedToTheFloor() =>
        // 20% of 1200 is 240; a sixth of the screen is 320, and the larger of the two is the floor.
        Assert.AreEqual(320, InlineCardMetrics.ResolveDockedWidth(1200, 1920, 20), 0.001);

    [TestMethod]
    public void ResolveDockedWidth_APercentTooLargeIsHeldToNinetyPercentOfTheScreen() =>
        Assert.AreEqual(1728, InlineCardMetrics.ResolveDockedWidth(1920, 1920, 100), 0.001);

    [TestMethod]
    public void ResolveDockedWidth_NeverWiderThanTheWindowItDocksTo() =>
        // The floor (a sixth of the screen, 320) does not fit inside a 300-wide window. The window wins: a
        // card that overhangs the window it belongs to is the thing this setting exists to stop.
        Assert.AreEqual(300, InlineCardMetrics.ResolveDockedWidth(300, 1920, 100), 0.001);

    [TestMethod]
    public void ResolveDockedWidth_NumbersOutsideTheBoxAreReadAsTheNearestEdge()
    {
        // user-settings.json is hand-editable, so the 20-100 the settings box takes is enforced here too: 150
        // reads as 100 and 5 as 20, and both then pass the screen's own bounds.
        Assert.AreEqual(1728, InlineCardMetrics.ResolveDockedWidth(1920, 1920, 150), 0.001);
        Assert.AreEqual(384, InlineCardMetrics.ResolveDockedWidth(1920, 1920, 5), 0.001);
    }

    // The desktop card keeps its own share and never reaches this math. A screen width of 0 is the degenerate
    // read -- nothing was measurable for the monitor -- and then only the window's own shares bound the card.
    // The desktop card keeps its own share and never reaches this math. A screen width of 0 is the degenerate
    // read -- nothing was measurable for the monitor -- and then only the window's own shares bound the card.
    [TestMethod]
    public void ResolveDockedWidth_WithNoScreenReadable_UsesOnlyTheWindowsOwnShares()
    {
        Assert.AreEqual(480, InlineCardMetrics.ResolveDockedWidth(1200, 0, null), 0.001);
        Assert.AreEqual(240, InlineCardMetrics.ResolveDockedWidth(1200, 0, 20), 0.001);
    }

    [TestMethod]
    public void CanApplyWhileSearching_ASettledSearchAlwaysApplies() =>
        Assert.IsTrue(InlineCardMetrics.CanApplyWhileSearching(false, false, null, false));

    [TestMethod]
    public void CanApplyWhileSearching_ASummonIsNeverVetoed() =>
        // The summon is the only pass that runs before the card is on screen: vetoing it leaves the card at the
        // XAML's own 550 DIP, which is the slab that read as a black frame.
        Assert.IsTrue(InlineCardMetrics.CanApplyWhileSearching(true, true, null, false));

    [TestMethod]
    public void CanApplyWhileSearching_RowsThatAreStillArrivingAreNotASize() =>
        Assert.IsFalse(InlineCardMetrics.CanApplyWhileSearching(true, false, null, true));

    [TestMethod]
    public void CanApplyWhileSearching_AFrozenAreaAppliesWhatTheHostOrBannerChanged()
    {
        // Six rows frozen and the list drawn: the height no longer depends on the rows arriving, so a new host,
        // a smaller monitor or a shown banner must apply mid-search instead of waiting for a settle that a
        // cancelled search never reaches.
        Assert.IsTrue(InlineCardMetrics.CanApplyWhileSearching(true, false, 6, true));
    }

    [TestMethod]
    public void CanApplyWhileSearching_AFrozenAreaWithNoListDrawnStillWaits() =>
        // A frozen count with the panel collapsed would charge the search bar alone and then grow back when the
        // first rows land -- the shrink-then-grow flash the veto exists to prevent.
        Assert.IsFalse(InlineCardMetrics.CanApplyWhileSearching(true, false, 6, false));
}
