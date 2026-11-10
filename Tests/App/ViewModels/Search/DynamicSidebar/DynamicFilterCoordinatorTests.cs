using Lertaro.PluginSdk.Abstractions;
using Lertaro.App.ViewModels.Search.DynamicSidebar;

namespace Lertaro.App.Tests.ViewModels.Search.DynamicSidebar;

[TestClass]
public sealed class DynamicFilterCoordinatorTests
{
    private static AppSearchResult Result(string path) => new() { FullPath = path, Name = path };

    private static List<AppSearchResult> IdentitySort(IEnumerable<AppSearchResult> items) => items.ToList();

    [TestMethod]
    public void Apply_NoActiveFilters_RendersSortedResultsAndClearsBusyIndicator()
    {
        var coordinator = new DynamicFilterCoordinator();
        var all = new List<AppSearchResult> { Result(@"C:\a") };
        var renders = new List<List<AppSearchResult>>();
        var busyStates = new List<bool>();

        coordinator.Apply(all, new List<Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>>>(),
            IdentitySort, () => all, renders.Add, busyStates.Add);

        Assert.HasCount(1, renders);
        Assert.AreEqual(@"C:\a", renders[0][0].FullPath);
        Assert.HasCount(1, busyStates);
        Assert.IsFalse(busyStates[0]);
    }

    [TestMethod]
    public async Task Apply_WithFilters_OnlySortsAndRendersTheFilteredAnswer()
    {
        var coordinator = new DynamicFilterCoordinator();
        var all = new List<AppSearchResult> { Result(@"C:\a"), Result(@"C:\b") };
        var renders = new List<List<AppSearchResult>>();
        var busyDone = new TaskCompletionSource();
        var sortedSizes = new List<int>();
        // Gated so the filter's await genuinely suspends (rather than completing synchronously like
        // Task.FromResult would), letting the test observe the pre-resolution render before continuing.
        var filterGate = new TaskCompletionSource<IReadOnlyList<ISearchResult>>();

        coordinator.Apply(all, new List<Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>>> { _ => filterGate.Task },
            rows => { var list = rows.ToList(); sortedSizes.Add(list.Count); return list; },
            () => all, renders.Add, busy => { if (!busy) busyDone.TrySetResult(); });

        Assert.IsEmpty(renders, "Do not flash rows rejected by the selected filter.");
        Assert.IsEmpty(sortedSizes);

        filterGate.SetResult(all.Where(r => r.FullPath == @"C:\a").ToList());
        await busyDone.Task;

        Assert.AreEqual(@"C:\a", Assert.ContainsSingle(Assert.ContainsSingle(renders)).FullPath);
        Assert.AreEqual(1, Assert.ContainsSingle(sortedSizes));
    }

    [TestMethod]
    public async Task Apply_StaleResultsSnapshot_SkipsFilteredRender()
    {
        var coordinator = new DynamicFilterCoordinator();
        var all = new List<AppSearchResult> { Result(@"C:\a") };
        var renders = new List<List<AppSearchResult>>();
        var busyDone = new TaskCompletionSource();
        var filterGate = new TaskCompletionSource<IReadOnlyList<ISearchResult>>();
        var currentSnapshot = all;

        coordinator.Apply(all, new List<Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>>> { _ => filterGate.Task },
            IdentitySort, () => currentSnapshot, renders.Add, busy => { if (!busy) busyDone.TrySetResult(); });

        // Simulate a newer search superseding this one before the filter resolves.
        currentSnapshot = new List<AppSearchResult> { Result(@"C:\newer") };
        filterGate.SetResult(all);

        await busyDone.Task;

        Assert.IsEmpty(renders);
    }
}
