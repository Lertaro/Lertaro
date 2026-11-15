using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class SearchResultRefreshWatcherTests
{
    [TestMethod]
    public void ResolveWatchedDirectories_UsesParentsAndIgnoresSyntheticRows()
    {
        var watched = SearchResultRefreshWatcher.ResolveWatchedDirectories(
        [
            new AppSearchResult { FullPath = @"C:\Projects\file.txt" },
            new AppSearchResult { FullPath = @"D:\Shared\folder\", IsDir = true },
            new AppSearchResult { FullPath = "__SHOW_MORE__" },
            new AppSearchResult { FullPath = "flow-preview:abc" },
        ]);

        CollectionAssert.AreEquivalent(new[] { @"C:\Projects", @"D:\Shared" }, watched.ToArray());
    }

    [TestMethod]
    public void ShouldRefresh_MatchesTheDirectoryContainingAResult()
    {
        Assert.IsTrue(SearchResultRefreshWatcher.ShouldRefresh(
            [@"C:\Projects"],
            [@"C:\Projects"]));
        Assert.IsTrue(SearchResultRefreshWatcher.ShouldRefresh(
            [@"C:\Projects\child"],
            [@"C:\Projects"]));
    }

    [TestMethod]
    public void ShouldRefresh_IgnoresUnrelatedDirectoriesAndOtherRoots()
    {
        Assert.IsFalse(SearchResultRefreshWatcher.ShouldRefresh(
            [@"C:\Other"],
            [@"C:\Projects"]));
        Assert.IsFalse(SearchResultRefreshWatcher.ShouldRefresh(
            [@"D:\"],
            [@"C:\Projects"]));
    }

    [TestMethod]
    public void ShouldRefresh_RootNotificationIsConservativeForTheSameRoot()
    {
        Assert.IsTrue(SearchResultRefreshWatcher.ShouldRefresh(
            [@"C:\"],
            [@"C:\Projects"]));
        Assert.IsFalse(SearchResultRefreshWatcher.ShouldRefresh(
            [@"D:\"],
            [@"C:\Projects"]));
    }
}
