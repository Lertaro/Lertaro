using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

/// <summary>
/// Arrow-key movement through a list whose rows are not all selectable. The case that matters most is
/// the one that used to hang: nothing selected, and nothing selectable to move to.
/// </summary>
[TestClass]
public class ListSelectionNavigatorTests
{
    private const int Down = 1;
    private const int Up = -1;

    // "s" marks a selectable row, anything else a header/separator/placeholder.
    private static int Next(string layout, int currentIndex, int direction) =>
        ListSelectionNavigator.NextSelectable(currentIndex, direction, layout.Length, i => layout[i] == 's');

    [TestMethod]
    public void NothingSelectableAndNothingSelectedReturnsNoSelection()
    {
        // The hang: SelectedIndex is -1 for a moment every time the results are replaced, and a list of
        // only headers or only the "no results" row has nothing to land on. The walk used to stop when
        // it returned to the index it started from, which -1 can never be, so it never stopped at all.
        Assert.AreEqual(-1, Next("---", -1, Down));
        Assert.AreEqual(-1, Next("---", -1, Up));
        Assert.AreEqual(-1, Next("-", -1, Down));
    }

    [TestMethod]
    public void NothingSelectableWithSomethingSelectedReturnsNoSelection()
    {
        Assert.AreEqual(-1, Next("---", 1, Down));
        Assert.AreEqual(-1, Next("---", 1, Up));
    }

    [TestMethod]
    public void WithNothingSelectedMovingDownStartsAtTheTop()
    {
        Assert.AreEqual(0, Next("sss", -1, Down));
        Assert.AreEqual(1, Next("-ss", -1, Down));
    }

    [TestMethod]
    public void WithNothingSelectedMovingUpStartsAtTheBottom()
    {
        Assert.AreEqual(2, Next("sss", -1, Up));
        Assert.AreEqual(1, Next("ss-", -1, Up));
    }

    [TestMethod]
    public void SkipsOverRowsThatCannotHoldTheSelection()
    {
        Assert.AreEqual(3, Next("s--s", 0, Down));
        Assert.AreEqual(0, Next("s--s", 3, Up));
    }

    [TestMethod]
    public void WrapsAroundEitherEnd()
    {
        Assert.AreEqual(0, Next("sss", 2, Down));
        Assert.AreEqual(2, Next("sss", 0, Up));
        Assert.AreEqual(1, Next("-ss", 2, Down), "wrapping past the end skips the header at the top");
    }

    [TestMethod]
    public void TheOnlySelectableRowIsNotReselected()
    {
        // Coming full circle leaves the selection alone, so holding an arrow key on a one-result list
        // does not re-scroll it on every repeat.
        Assert.AreEqual(-1, Next("-s-", 1, Down));
        Assert.AreEqual(-1, Next("-s-", 1, Up));
    }

    [TestMethod]
    public void AnOutOfRangeCurrentIndexIsTreatedAsNoSelection()
    {
        // The list can shrink between the key press and this running.
        Assert.AreEqual(0, Next("ss", 9, Down));
        Assert.AreEqual(1, Next("ss", 9, Up));
    }

    [TestMethod]
    public void AnEmptyListSelectsNothing()
    {
        Assert.AreEqual(-1, ListSelectionNavigator.NextSelectable(-1, Down, 0, _ => true));
        Assert.AreEqual(-1, ListSelectionNavigator.NextSelectable(0, Down, 0, _ => true));
    }

    [TestMethod]
    public void ADirectionOfZeroMovesNowhere() => Assert.AreEqual(-1, ListSelectionNavigator.NextSelectable(0, 0, 3, _ => true));

    [TestMethod]
    public void NeverTestsMoreRowsThanTheListHolds()
    {
        // The real guarantee: whatever the starting index, the walk is bounded. Without it the UI thread
        // spun here forever.
        foreach (var start in new[] { -5, -1, 0, 1, 2, 7 })
        {
            var probes = 0;
            ListSelectionNavigator.NextSelectable(start, Down, 3, _ => { probes++; return false; });
            Assert.IsLessThanOrEqualTo(3, probes, $"unbounded walk from index {start}");
        }
    }
}
