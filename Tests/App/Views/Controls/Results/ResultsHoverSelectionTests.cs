using Lertaro.App.Views.Controls.Results;
using Point = System.Windows.Point;

namespace Lertaro.App.Tests.Views.Controls.Results;

[TestClass]
public sealed class ResultsHoverSelectionTests
{
    [TestMethod]
    public void UpdatePointerPosition_FirstObservation_SeedsWithoutMovement()
    {
        Point? previous = null;

        var moved = ResultsHoverSelection.UpdatePointerPosition(ref previous, new Point(100, 200));

        Assert.IsFalse(moved);
        Assert.AreEqual(new Point(100, 200), previous);
    }

    [TestMethod]
    public void UpdatePointerPosition_UnchangedScreenPosition_IsNotMovement()
    {
        Point? previous = new Point(100, 200);

        var moved = ResultsHoverSelection.UpdatePointerPosition(ref previous, new Point(100, 200));

        Assert.IsFalse(moved);
    }

    [TestMethod]
    public void UpdatePointerPosition_ChangedScreenPosition_IsMovement()
    {
        Point? previous = new Point(100, 200);

        var moved = ResultsHoverSelection.UpdatePointerPosition(ref previous, new Point(101, 200));

        Assert.IsTrue(moved);
        Assert.AreEqual(new Point(101, 200), previous);
    }
}
