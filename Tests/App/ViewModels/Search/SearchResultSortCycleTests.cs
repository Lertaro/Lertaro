using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class SearchResultSortCycleTests
{
    [TestMethod]
    public void Advance_DifferentColumn_SwitchesToItAscending()
    {
        var (column, isAscending) = SearchResultSortCycle.Advance("Name", false, "Path");

        Assert.AreEqual("Path", column);
        Assert.IsTrue(isAscending);
    }

    [TestMethod]
    public void Advance_NoColumnActiveYet_SwitchesToClickedColumnAscending()
    {
        var (column, isAscending) = SearchResultSortCycle.Advance(string.Empty, true, "Name");

        Assert.AreEqual("Name", column);
        Assert.IsTrue(isAscending);
    }

    [TestMethod]
    public void Advance_SameColumnAscending_SwitchesToDescending()
    {
        var (column, isAscending) = SearchResultSortCycle.Advance("Name", true, "Name");

        Assert.AreEqual("Name", column);
        Assert.IsFalse(isAscending);
    }

    // The third click on the same column resets to the default relevance-ranked order (empty column),
    // rather than toggling ascending/descending forever with no way back to the default.
    [TestMethod]
    public void Advance_SameColumnDescending_ResetsToDefaultOrder()
    {
        var (column, isAscending) = SearchResultSortCycle.Advance("Name", false, "Name");

        Assert.AreEqual(string.Empty, column);
        Assert.IsTrue(isAscending);
    }

    [TestMethod]
    public void Advance_FullCycleOnOneColumn_ReturnsToDefaultAfterThreeClicks()
    {
        var state = (Column: string.Empty, IsAscending: true);

        state = SearchResultSortCycle.Advance(state.Column, state.IsAscending, "Name");
        Assert.AreEqual("Name", state.Column);
        Assert.IsTrue(state.IsAscending);

        state = SearchResultSortCycle.Advance(state.Column, state.IsAscending, "Name");
        Assert.AreEqual("Name", state.Column);
        Assert.IsFalse(state.IsAscending);

        state = SearchResultSortCycle.Advance(state.Column, state.IsAscending, "Name");
        Assert.AreEqual(string.Empty, state.Column);
        Assert.IsTrue(state.IsAscending);
    }
}
