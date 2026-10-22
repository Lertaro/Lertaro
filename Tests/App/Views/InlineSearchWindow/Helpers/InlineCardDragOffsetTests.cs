using Lertaro.App.Views.InlineSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

// A drag the user performed on the inline card lives on as a displacement from the position the card docks to,
// one per host application. What these pin is that the displacement is scaled by the monitor's DPI (it is
// recorded in DIP while the dock position is in physical pixels), that the result stays reachable on the working
// area, and that a displacement belongs to the host it was dragged off.
[TestClass]
public sealed class InlineCardDragOffsetTests
{
    private static readonly System.Drawing.Rectangle Screen1080 = new(0, 0, 1920, 1080);

    [TestMethod]
    public void Apply_MovesTheCardByTheDisplacement()
    {
        var (left, top) = InlineCardDragOffset.Apply(
            left: 1500, top: 500, offsetX: -200, offsetY: -120, dpiScaleX: 1.0, dpiScaleY: 1.0,
            workingArea: Screen1080, windowWidth: 465, windowHeight: 550);

        Assert.AreEqual(1300.0, left, 0.001);
        Assert.AreEqual(380.0, top, 0.001);
    }

    [TestMethod]
    public void Apply_ScalesTheDisplacementByTheMonitorsDpi()
    {
        var (left, top) = InlineCardDragOffset.Apply(
            left: 3000, top: 1000, offsetX: -300, offsetY: -120, dpiScaleX: 2.0, dpiScaleY: 1.5,
            workingArea: new System.Drawing.Rectangle(0, 0, 3840, 2160), windowWidth: 930, windowHeight: 825);

        Assert.AreEqual(2400.0, left, 0.001);
        Assert.AreEqual(820.0, top, 0.001);
    }

    [TestMethod]
    public void Apply_DraggedPastTheTopLeftCorner_IsPinnedToTheWorkingArea()
    {
        // A later re-dock (the dialog moved to another monitor) can move the position the displacement is
        // added to; the card has to stay reachable rather than end up half off screen.
        var (left, top) = InlineCardDragOffset.Apply(
            left: 100, top: 100, offsetX: -900, offsetY: -900, dpiScaleX: 1.0, dpiScaleY: 1.0,
            workingArea: Screen1080, windowWidth: 465, windowHeight: 550);

        Assert.AreEqual(0.0, left, 0.001);
        Assert.AreEqual(0.0, top, 0.001);
    }

    [TestMethod]
    public void Apply_DraggedPastTheBottomRightCorner_KeepsTheWholeCardOnScreen()
    {
        // The far edge is the one that matters: the card's bottom-right corner (not its top-left) is what must
        // not leave the area, so the bound is the area's size minus the card's own.
        var (left, top) = InlineCardDragOffset.Apply(
            left: 1000, top: 400, offsetX: 900, offsetY: 900, dpiScaleX: 1.0, dpiScaleY: 1.0,
            workingArea: Screen1080, windowWidth: 465, windowHeight: 550);

        Assert.AreEqual(1920.0 - 465, left, 0.001);
        Assert.AreEqual(1080.0 - 550, top, 0.001);
    }

    [TestMethod]
    public void Apply_NoWorkingAreaToClampAgainst_StillAppliesTheDisplacement()
    {
        // No tracked window and not the desktop: there is no screen to clamp against, and dropping the drag
        // would be worse than moving the card where the user put it.
        var (left, top) = InlineCardDragOffset.Apply(
            left: 100, top: 100, offsetX: 40, offsetY: -30, dpiScaleX: 1.0, dpiScaleY: 1.0,
            workingArea: System.Drawing.Rectangle.Empty, windowWidth: 465, windowHeight: 550);

        Assert.AreEqual(140.0, left, 0.001);
        Assert.AreEqual(70.0, top, 0.001);
    }

    // The tests above take the displacement as an argument. The ones below are the reason the displacement is
    // remembered per host application at all: the card is ONE window, parked rather than destroyed when it is
    // dismissed (InlineSearchWindowSummonSupport), so a single global displacement travelled with it from host
    // to host -- moving the card inside Directory Opus moved the card inside Explorer.

    [TestMethod]
    public void ADragRecordedForOneHost_DoesNotMoveAnotherHostsCard()
    {
        var left = 1500.0;
        var top = 500.0;
        var offsets = new InlineCardDragOffset(() => (left, top));

        offsets.RememberBase("dopus", 1500, 500);
        (left, top) = (1300, 380);
        offsets.RememberDrag();

        Assert.IsTrue(offsets.IsSetFor("dopus"));
        Assert.IsFalse(offsets.IsSetFor("explorer"));
        Assert.AreEqual((1500.0, 500.0), offsets.ApplyPhysical(
            "explorer", 1500, 500, 1.0, 1.0, Screen1080, 465, 550));
    }

    [TestMethod]
    public void EachHostKeepsItsOwnDisplacementThroughAReDock()
    {
        var left = 1500.0;
        var top = 500.0;
        var offsets = new InlineCardDragOffset(() => (left, top));

        offsets.RememberBase("dopus", 1500, 500);
        (left, top) = (1300, 380);
        offsets.RememberDrag();

        // Explorer's card is then dragged the other way; neither one may pick up the other's travel.
        offsets.RememberBase("explorer", 900, 300);
        (left, top) = (1000, 400);
        offsets.RememberDrag();

        Assert.AreEqual((1300.0, 380.0), offsets.ApplyPhysical(
            "dopus", 1500, 500, 1.0, 1.0, Screen1080, 465, 550));
        Assert.AreEqual((1000.0, 400.0), offsets.ApplyPhysical(
            "explorer", 900, 300, 1.0, 1.0, Screen1080, 465, 550));
    }

    [TestMethod]
    public void ADragIsAppliedToItsOwnHostScaledByThatPassesDpi()
    {
        // Recorded against a dock of 1500,500 and left at 1200,380: -300,-120 DIP, which on a 200%/150% pass is
        // -600,-180 physical -- the same arithmetic the static Apply is tested with, reached through the host.
        var offsets = new InlineCardDragOffset(() => (1200.0, 380.0));
        offsets.RememberBase("dopus", 1500, 500);
        offsets.RememberDrag();

        Assert.AreEqual((2400.0, 820.0), offsets.ApplyPhysical(
            "dopus", 3000, 1000, 2.0, 1.5, new System.Drawing.Rectangle(0, 0, 3840, 2160), 930, 825));
    }

    [TestMethod]
    public void ClearFor_DropsOneHostsDisplacementAndLeavesTheOthers()
    {
        // What the logo's right click does: the card the user is looking at goes back to where it docks, while a
        // displacement dragged in another application stays where that user left it.
        var offsets = new InlineCardDragOffset(() => (1300.0, 380.0));
        offsets.RememberBase("dopus", 1500, 500);
        offsets.RememberDrag();
        offsets.RememberBase("explorer", 900, 300);
        offsets.RememberDrag();

        offsets.ClearFor("explorer");

        Assert.IsFalse(offsets.IsSetFor("explorer"));
        Assert.IsTrue(offsets.IsSetFor("dopus"), "resetting one host does not reset another");
        Assert.AreEqual((900.0, 300.0), offsets.ApplyPhysical(
            "explorer", 900, 300, 1.0, 1.0, Screen1080, 465, 550), "the dock position, with nothing added to it");
        Assert.AreEqual((700.0, 180.0), offsets.ApplyPhysical(
            "dopus", 900, 300, 1.0, 1.0, Screen1080, 465, 550), "dopus still carries its own -200,-120");
    }
}
