using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

// Which side of its owner the preview docks to. The rest of the geometry needs a real Window on a real
// monitor; this is the decision inside it that does not.
[TestClass]
public sealed class QuickLookDockSideTests
{
    private const double Needed = 700;

    [TestMethod]
    public void TheRightFits_DocksRight() => Assert.IsTrue(QuickLookManager.ChooseRightSide(roomRight: 800, Needed));

    // Exactly enough still counts: the gap is already in the room figure the caller works out.
    [TestMethod]
    public void TheRightFitsExactly_DocksRight() => Assert.IsTrue(QuickLookManager.ChooseRightSide(roomRight: Needed, Needed));

    // A panel docked into the bottom-right corner of a maximized window: hard against the screen edge,
    // so the flip to the left follows from the fit alone.
    [TestMethod]
    public void NoRoomOnTheRight_DocksLeft() => Assert.IsFalse(QuickLookManager.ChooseRightSide(roomRight: 12, Needed));

    // The rule deliberately does NOT compare the two sides. Having more space on the left is not a
    // reason to move a preview that fits perfectly well on the right -- that was tried, and it sent the
    // preview across the panel with the whole right of the screen sitting empty.
    [TestMethod]
    public void TheRightFitsButTheLeftIsWider_StillDocksRight()
        => Assert.IsTrue(QuickLookManager.ChooseRightSide(roomRight: 800, Needed));
}
