using System.Windows.Threading;
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
    public void ResolveWatchedDirectories_IgnoresDriveRootRows()
    {
        // A row that is itself a drive root has no containing folder; watching the root would match every
        // change on the drive.
        var watched = SearchResultRefreshWatcher.ResolveWatchedDirectories(
        [
            @"C:\",
            @"D:\Projects\file.txt",
        ]);

        CollectionAssert.AreEquivalent(new[] { @"D:\Projects" }, watched.ToArray());
    }

    [TestMethod]
    public void ResolveWatchedDirectories_IgnoresProviderValuesThatOnlyLookQualified()
    {
        var watched = SearchResultRefreshWatcher.ResolveWatchedDirectories(
        [
            "photo:2024 trip",
            @"C:\Projects\file.txt",
        ]);

        CollectionAssert.AreEquivalent(new[] { @"C:\Projects" }, watched.ToArray());
    }

    [TestMethod]
    public void ResolveWatchedDirectories_EmptyResultSetWatchesNothing()
    {
        Assert.AreEqual(0, SearchResultRefreshWatcher.ResolveWatchedDirectories(Array.Empty<string>()).Count);
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
        // The parent of a watched folder is not part of it, and neither is a sibling that shares a prefix.
        Assert.IsFalse(SearchResultRefreshWatcher.ShouldRefresh(
            [@"C:\Projects"],
            [@"C:\Projects\Sub"]));
        Assert.IsFalse(SearchResultRefreshWatcher.ShouldRefresh(
            [@"C:\Projects2"],
            [@"C:\Projects"]));
    }

    [TestMethod]
    public void ShouldRefresh_IgnoresEverythingWhileNoResultsAreShown()
    {
        Assert.IsFalse(SearchResultRefreshWatcher.ShouldRefresh([@"C:\Projects"], Array.Empty<string>()));
    }

    [STATestMethod]
    public void RunRefresh_WaitsForTheRunningSearchToSettle()
    {
        var refreshes = 0;
        var searching = true;
        using var watcher = new SearchResultRefreshWatcher(
            () => [new AppSearchResult { FullPath = @"C:\Projects\a.txt" }],
            () => refreshes++,
            isSearchRunning: () => searching,
            Dispatcher.CurrentDispatcher);

        // Refreshing reruns the query, and a query cancels the one in flight, so this must not fire yet.
        watcher.RunRefresh();
        Assert.AreEqual(0, refreshes);

        searching = false;
        watcher.UpdateResults(Array.Empty<AppSearchResult>());
        PumpFor(TimeSpan.FromSeconds(1.5));

        Assert.AreEqual(1, refreshes);
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

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var stop = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher) { Interval = duration };
        stop.Tick += (_, _) =>
        {
            stop.Stop();
            frame.Continue = false;
        };
        stop.Start();
        Dispatcher.PushFrame(frame);
    }
}
