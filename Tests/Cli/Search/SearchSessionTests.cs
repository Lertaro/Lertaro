using Lertaro.Core;
using Lertaro.Cli.Search;

namespace Lertaro.Cli.Tests.Search;

[TestClass]
public sealed class SearchSessionTests
{
    private static SearchSession CreateSession() => new("test-pipe");

    private static (SearchResult, int[])[] MakeResults(int count) =>
        Enumerable.Range(0, count)
            .Select(i => (new SearchResult { Name = $"file{i}.txt", Path = $@"c:\file{i}.txt" }, Array.Empty<int>()))
            .ToArray();

    [TestMethod]
    public void MoveHighlight_EmptyResults_IsNoOp()
    {
        var session = CreateSession();

        session.MoveHighlight(1);

        Assert.AreEqual(0, session.HighlightIndex);
        Assert.AreEqual(0, session.ViewOffset);
    }

    [TestMethod]
    public void MoveHighlight_WithinVisibleWindow_MovesWithoutScrolling()
    {
        var session = CreateSession();
        session.SetResultsForTests(MakeResults(5));

        session.MoveHighlight(2);

        Assert.AreEqual(2, session.HighlightIndex);
        Assert.AreEqual(0, session.ViewOffset);
    }

    [TestMethod]
    public void MoveHighlight_PastLastResult_ClampsToLastIndex()
    {
        var session = CreateSession();
        session.SetResultsForTests(MakeResults(5));

        session.MoveHighlight(100);

        Assert.AreEqual(4, session.HighlightIndex);
    }

    [TestMethod]
    public void MoveHighlight_BeforeFirstResult_ClampsToZero()
    {
        var session = CreateSession();
        session.SetResultsForTests(MakeResults(5));

        session.MoveHighlight(-100);

        Assert.AreEqual(0, session.HighlightIndex);
    }

    [TestMethod]
    public void MoveHighlight_PastVisibleWindow_ScrollsViewOffsetDown()
    {
        var session = CreateSession();
        session.SetResultsForTests(MakeResults(20)); // more than MaxVisible (15)

        session.MoveHighlight(19); // to the last result

        Assert.AreEqual(19, session.HighlightIndex);
        Assert.AreEqual(5, session.ViewOffset); // 20 - MaxVisible(15) = 5, the max possible offset
    }

    [TestMethod]
    public void MoveHighlight_BackAboveViewOffset_ScrollsViewOffsetUp()
    {
        var session = CreateSession();
        session.SetResultsForTests(MakeResults(20));
        session.MoveHighlight(19); // scrolls down to the bottom first

        session.MoveHighlight(-19); // back to the top

        Assert.AreEqual(0, session.HighlightIndex);
        Assert.AreEqual(0, session.ViewOffset);
    }

    [TestMethod]
    public void ToggleSelectionAtHighlight_EmptyResults_IsNoOp()
    {
        var session = CreateSession();

        session.ToggleSelectionAtHighlight();

        Assert.IsEmpty(session.Selected);
    }

    [TestMethod]
    public void ToggleSelectionAtHighlight_MarksTheHighlightedPath()
    {
        var session = CreateSession();
        session.SetResultsForTests(MakeResults(5));

        session.ToggleSelectionAtHighlight();

        CollectionAssert.Contains(session.Selected.ToList(), @"c:\file0.txt");
    }

    [TestMethod]
    public void ToggleSelectionAtHighlight_CalledTwiceOnSameRow_TogglesBackOff()
    {
        // A single result means MoveHighlight(1)'s own clamp keeps landing back on row 0, so both
        // toggles hit the same path.
        var session = CreateSession();
        session.SetResultsForTests(MakeResults(1));

        session.ToggleSelectionAtHighlight();
        session.ToggleSelectionAtHighlight();

        Assert.IsEmpty(session.Selected);
    }

    [TestMethod]
    public void GetChosenPaths_NoResultsNoSelection_ReturnsEmpty()
    {
        var session = CreateSession();

        Assert.IsEmpty(session.GetChosenPaths());
    }

    [TestMethod]
    public void GetChosenPaths_NoSelection_ReturnsOnlyTheHighlightedPath()
    {
        var session = CreateSession();
        session.SetResultsForTests(MakeResults(5));
        session.MoveHighlight(2);

        CollectionAssert.AreEqual(new[] { @"c:\file2.txt" }, session.GetChosenPaths().ToList());
    }

    [TestMethod]
    public void GetChosenPaths_WithSelections_ReturnsSelectedPathsOrderedIgnoringHighlight()
    {
        var session = CreateSession();
        session.SetResultsForTests(MakeResults(3)); // c:\file0.txt, c:\file1.txt, c:\file2.txt
        session.MoveHighlight(2);
        session.ToggleSelectionAtHighlight(); // selects file2.txt (also advances highlight, clamped back to 2)
        session.MoveHighlight(-2);
        session.ToggleSelectionAtHighlight(); // selects file0.txt

        var chosen = session.GetChosenPaths().ToList();

        CollectionAssert.AreEqual(new[] { @"c:\file0.txt", @"c:\file2.txt" }, chosen);
    }
}
