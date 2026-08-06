using System.Windows;
using Lertaro.App.Helpers.Visuals;

namespace Lertaro.App.Tests.Helpers;

// The rule a rubber band applies: which items the box covers, and what happens to whatever was already
// selected when it started. Driven through Resolve rather than a real drag -- the geometry is the part
// with decisions in it; the capture and the adorner are plumbing around it.
[TestClass]
public sealed class RubberBandSelectionTests
{
    // Three tiles in a row, 84 wide as the panel draws them, with a gap between each.
    private static readonly Rect[] Tiles =
    {
        new(0, 0, 84, 90),
        new(90, 0, 84, 90),
        new(180, 0, 84, 90),
    };

    private static bool[] None => new[] { false, false, false };

    [TestMethod]
    public void Resolve_TakesEveryItemTheBoxTouches()
    {
        // Across the middle of the first two tiles, stopping short of the third.
        var wanted = RubberBandSelection.Resolve(new Rect(10, 40, 120, 10), Tiles, None, additive: false);

        CollectionAssert.AreEqual(new[] { true, true, false }, wanted);
    }

    // Touching, not containing: dragging across a row of tiles takes that row, which is what the gesture
    // looks like it should do. Requiring full containment means a careful drag selects nothing.
    [TestMethod]
    public void Resolve_ClippingATilesCornerIsEnough()
        => CollectionAssert.AreEqual(
            new[] { false, false, true },
            RubberBandSelection.Resolve(new Rect(175, 85, 10, 10), Tiles, None, additive: false));

    [TestMethod]
    public void Resolve_BoxOverNothing_SelectsNothing()
        => CollectionAssert.AreEqual(
            None,
            RubberBandSelection.Resolve(new Rect(0, 200, 300, 20), Tiles, None, additive: false));

    // Without a modifier the box IS the selection, so dragging away from something drops it.
    [TestMethod]
    public void Resolve_WithoutCtrl_ReplacesWhatWasSelected()
        => CollectionAssert.AreEqual(
            new[] { false, false, true },
            RubberBandSelection.Resolve(new Rect(185, 10, 10, 10), Tiles, new[] { true, true, false }, additive: false));

    [TestMethod]
    public void Resolve_WithCtrl_AddsToWhatWasSelected()
        => CollectionAssert.AreEqual(
            new[] { true, true, true },
            RubberBandSelection.Resolve(new Rect(185, 10, 10, 10), Tiles, new[] { true, true, false }, additive: true));

    // A container that does not exist has no bounds, and an empty rectangle must not read as one sitting
    // at the origin -- a box drawn in the top-left corner would otherwise sweep up every missing item.
    [TestMethod]
    public void Resolve_ItemWithNoBounds_IsNeverCovered()
    {
        var bounds = new[] { Rect.Empty, new Rect(90, 0, 84, 90) };

        var wanted = RubberBandSelection.Resolve(new Rect(0, 0, 200, 200), bounds, new[] { false, false }, additive: false);

        CollectionAssert.AreEqual(new[] { false, true }, wanted);
    }

    // The strip of state handed in is whatever the list held when the drag began; a list that grew since
    // must not throw over the items that state cannot speak for.
    [TestMethod]
    public void Resolve_ShorterPriorState_IsTreatedAsUnselected()
        => CollectionAssert.AreEqual(
            new[] { true, false, false },
            RubberBandSelection.Resolve(new Rect(0, 0, 10, 10), Tiles, new[] { true }, additive: true));
}
