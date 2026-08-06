using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

// Resets the shared static before/after each test -- this holder is deliberately process-wide (see its
// own comment), so a leftover value from one test would otherwise leak into whichever test runs next.
[TestClass]
[DoNotParallelize]
public sealed class SearchResultSortMemoryTests
{
    [TestInitialize]
    public void Reset()
    {
        SearchResultSortMemory.CurrentSortColumn = string.Empty;
        SearchResultSortMemory.IsSortAscending = true;
    }

    [TestCleanup]
    public void Cleanup() => Reset();

    [TestMethod]
    public void CurrentSortColumn_DefaultsToEmpty() =>
        Assert.AreEqual(string.Empty, SearchResultSortMemory.CurrentSortColumn);

    [TestMethod]
    public void IsSortAscending_DefaultsToTrue() =>
        Assert.IsTrue(SearchResultSortMemory.IsSortAscending);

    [TestMethod]
    public void Values_PersistAcrossReads()
    {
        SearchResultSortMemory.CurrentSortColumn = "DateModified";
        SearchResultSortMemory.IsSortAscending = false;

        Assert.AreEqual("DateModified", SearchResultSortMemory.CurrentSortColumn);
        Assert.IsFalse(SearchResultSortMemory.IsSortAscending);
    }
}
