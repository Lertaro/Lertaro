using Lertaro.App.Views.InlineSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

// A drag the user performed on the inline card lives on as a displacement from the position the card docks
// to. What these pin is that the displacement is scaled by the monitor's DPI (it is recorded in DIP while the
// dock position is in physical pixels) and that the result stays reachable on the working area.
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
}
