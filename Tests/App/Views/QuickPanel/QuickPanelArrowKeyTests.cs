using Lertaro.App.Views.QuickPanel;

namespace Lertaro.App.Tests.Views.QuickPanel;

// Where one press of Up or Down lands, over the groups' items taken as one sequence. The counts are what
// each visible group's list holds, in the order they are drawn.
[TestClass]
public sealed class QuickPanelArrowKeyTests
{
    [TestMethod]
    public void Down_WithinAGroup_MovesToTheNextRow()
        => Assert.AreEqual((0, 1), QuickPanelWindow.NextPosition(new[] { 3, 2 }, list: 0, item: 0, delta: 1));

    [TestMethod]
    public void Down_OnTheLastRowOfAGroup_CrossesIntoTheNextGroup()
        => Assert.AreEqual((1, 0), QuickPanelWindow.NextPosition(new[] { 3, 2 }, list: 0, item: 2, delta: 1));

    [TestMethod]
    public void Up_OnTheFirstRowOfAGroup_CrossesBackToTheLastRowOfThePrevious()
        => Assert.AreEqual((0, 2), QuickPanelWindow.NextPosition(new[] { 3, 2 }, list: 1, item: 0, delta: -1));

    // A group can be on screen with nothing in it only briefly, but the sequence must not stop in one.
    [TestMethod]
    public void EmptyGroupsAreSteppedOverRatherThanStoppedIn()
    {
        Assert.AreEqual((2, 0), QuickPanelWindow.NextPosition(new[] { 1, 0, 2 }, list: 0, item: 0, delta: 1));
        Assert.AreEqual((0, 0), QuickPanelWindow.NextPosition(new[] { 1, 0, 2 }, list: 2, item: 0, delta: -1));
    }

    // Neither end wraps: this is a list being read, not a ring being cycled.
    [TestMethod]
    public void AtTheVeryBottom_DownGoesNowhere()
        => Assert.IsNull(QuickPanelWindow.NextPosition(new[] { 3, 2 }, list: 1, item: 1, delta: 1));

    [TestMethod]
    public void AtTheVeryTop_UpGoesNowhere()
        => Assert.IsNull(QuickPanelWindow.NextPosition(new[] { 3, 2 }, list: 0, item: 0, delta: -1));

    // The state a summon starts in, and the state a cleared selection leaves behind.
    [TestMethod]
    public void NothingSelected_StartsAtTheTop_WhicheverKeyWasPressed()
    {
        Assert.AreEqual((0, 0), QuickPanelWindow.NextPosition(new[] { 3, 2 }, list: -1, item: -1, delta: 1));
        Assert.AreEqual((0, 0), QuickPanelWindow.NextPosition(new[] { 3, 2 }, list: -1, item: -1, delta: -1));
    }

    // The group holding the selection was collapsed or filtered away while it held it.
    [TestMethod]
    public void SelectionInAGroupThatIsGone_StartsAtTheTop()
        => Assert.AreEqual((0, 0), QuickPanelWindow.NextPosition(new[] { 3 }, list: 4, item: 1, delta: 1));

    [TestMethod]
    public void NothingOnScreenAtAll_GoesNowhere()
    {
        Assert.IsNull(QuickPanelWindow.NextPosition(Array.Empty<int>(), list: -1, item: -1, delta: 1));
        Assert.IsNull(QuickPanelWindow.NextPosition(new[] { 0, 0 }, list: -1, item: -1, delta: 1));
    }

    [TestMethod]
    public void ThumbnailArrows_MoveVerticallyByColumnAndHorizontallyAcrossRows()
    {
        var counts = new[] { 6 };
        var columns = new[] { 3 };

        Assert.AreEqual((0, 3), QuickPanelWindow.NextThumbnailPosition(counts, columns, 0, 0, 1, 0));
        Assert.AreEqual((0, 3), QuickPanelWindow.NextThumbnailPosition(counts, columns, 0, 2, 0, 1));
        Assert.AreEqual((0, 1), QuickPanelWindow.NextThumbnailPosition(counts, columns, 0, 2, 0, -1));
    }

    [TestMethod]
    public void ThumbnailDown_StopsAtMissingColumnInShortFinalRow()
    {
        var next = QuickPanelWindow.NextThumbnailPosition(new[] { 5 }, new[] { 3 }, 0, 2, 1, 0);

        Assert.IsNull(next);
    }

    [TestMethod]
    public void ThumbnailVerticalGroupBoundary_UsesExistingCrossGroupTransition()
    {
        var next = QuickPanelWindow.NextThumbnailPosition(new[] { 3, 3 }, new[] { 3, 3 }, 0, 2, 1, 0);

        Assert.AreEqual((1, 2), next);
    }

    [TestMethod]
    public void ThumbnailDown_FromAnyTileInTheFinalRow_CrossesWithoutLinearFallback()
    {
        var next = QuickPanelWindow.NextThumbnailPosition(new[] { 5, 3 }, new[] { 3, 3 }, 0, 3, 1, 0);

        Assert.AreEqual((1, 0), next);
    }

    [TestMethod]
    public void ThumbnailUp_FromTheFirstRow_UsesTheSameColumnInThePreviousGroup()
    {
        var next = QuickPanelWindow.NextThumbnailPosition(new[] { 6, 3 }, new[] { 3, 3 }, 1, 0, -1, 0);

        Assert.AreEqual((0, 3), next);
    }
}
