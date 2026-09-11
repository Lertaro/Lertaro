namespace Lertaro.Core.Tests;

// The engine has no injectable index seam, so the decision it makes -- "which in-flight search does a
// new one supersede" -- lives in this registry and is pinned here instead. The multi-folder case is the
// regression: a scope over several folders issues one SearchDir request per folder concurrently, and a
// single shared slot made every request cancel its own siblings.
[TestClass]
public sealed class SearchCancellationRegistryTests
{
    [TestMethod]
    public void DifferentDirectoryFilters_DoNotCancelEachOther()
    {
        var registry = new SearchCancellationRegistry();

        var first = registry.Begin(@"C:\Users\testuser\Downloads");
        var second = registry.Begin(@"D:\Books");

        Assert.IsFalse(first.IsCancellationRequested, "a sibling folder's search must not cancel this one");
        Assert.IsFalse(second.IsCancellationRequested);
    }

    [TestMethod]
    public void ManyFolderFanOut_LeavesEverySearchRunning()
    {
        var registry = new SearchCancellationRegistry();
        var folders = new[] { @"C:\A", @"C:\B", @"C:\C", @"C:\D", @"C:\E" };

        var sources = folders.Select(registry.Begin).ToList();

        Assert.IsEmpty(sources.Where(s => s.IsCancellationRequested));
    }

    [TestMethod]
    public void SameDirectoryFilter_SupersedesTheRunningSearch()
    {
        var registry = new SearchCancellationRegistry();

        var older = registry.Begin(@"C:\Books");
        var newer = registry.Begin(@"C:\Books");

        Assert.IsTrue(older.IsCancellationRequested);
        Assert.IsFalse(newer.IsCancellationRequested);
    }

    [TestMethod]
    public void ScopedAndUnscopedSearches_AreIndependentSlots()
    {
        var registry = new SearchCancellationRegistry();

        var unscoped = registry.Begin(null);
        var scoped = registry.Begin(@"C:\Books");

        Assert.IsFalse(unscoped.IsCancellationRequested);
        Assert.IsFalse(scoped.IsCancellationRequested);
    }

    [TestMethod]
    public void UnscopedSearch_SupersedesOnlyThePreviousUnscopedSearch()
    {
        var registry = new SearchCancellationRegistry();

        var olderUnscoped = registry.Begin(null);
        var scoped = registry.Begin(@"C:\Books");
        var newerUnscoped = registry.Begin(null);
        var emptyFilter = registry.Begin(string.Empty);

        Assert.IsTrue(olderUnscoped.IsCancellationRequested);
        Assert.IsFalse(scoped.IsCancellationRequested, "an unscoped search must not touch a directory-filtered one");
        Assert.IsTrue(newerUnscoped.IsCancellationRequested, "an empty filter is the same slot as a null one");
        Assert.IsFalse(emptyFilter.IsCancellationRequested);
    }

    [TestMethod]
    public void DirectoryFilterCase_IsIgnoredWhenMatchingSlots()
    {
        var registry = new SearchCancellationRegistry();

        var older = registry.Begin(@"c:\books");
        var newer = registry.Begin(@"C:\Books");

        Assert.IsTrue(older.IsCancellationRequested);
        Assert.IsFalse(newer.IsCancellationRequested);
    }

    [TestMethod]
    public void End_ReleasesTheSlot_SoALaterSearchDoesNotCancelAFinishedOne()
    {
        var registry = new SearchCancellationRegistry();
        var finished = registry.Begin(@"C:\Books");
        registry.End(@"C:\Books", finished);

        var next = registry.Begin(@"C:\Books");

        Assert.IsFalse(finished.IsCancellationRequested, "a completed search is no longer the one to supersede");
        Assert.IsFalse(next.IsCancellationRequested);
    }

    [TestMethod]
    public void End_ByASupersededSearch_DoesNotReleaseTheNewerSearchsSlot()
    {
        var registry = new SearchCancellationRegistry();
        var superseded = registry.Begin(@"C:\Books");
        var current = registry.Begin(@"C:\Books");

        registry.End(@"C:\Books", superseded);
        var next = registry.Begin(@"C:\Books");

        Assert.IsTrue(current.IsCancellationRequested, "the superseded search's End must not drop the live slot");
        Assert.IsFalse(next.IsCancellationRequested);
    }

    [TestMethod]
    public void CancelAll_CancelsEverySlotIncludingUnscoped()
    {
        var registry = new SearchCancellationRegistry();
        var unscoped = registry.Begin(null);
        var scoped = registry.Begin(@"C:\Books");

        registry.CancelAll();

        Assert.IsTrue(unscoped.IsCancellationRequested);
        Assert.IsTrue(scoped.IsCancellationRequested);
    }
}
