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
    public async Task Apply_WithFilters_RendersUnfilteredImmediatelyThenFilteredAfterResolving()
    {
        var coordinator = new DynamicFilterCoordinator();
        var all = new List<AppSearchResult> { Result(@"C:\a"), Result(@"C:\b") };
        var renders = new List<List<AppSearchResult>>();
        var busyDone = new TaskCompletionSource();
        // Gated so the filter's await genuinely suspends (rather than completing synchronously like
        // Task.FromResult would), letting the test observe the pre-resolution render before continuing.
        var filterGate = new TaskCompletionSource<IReadOnlyList<ISearchResult>>();

        coordinator.Apply(all, new List<Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>>> { _ => filterGate.Task },
            IdentitySort, () => all, renders.Add, busy => { if (!busy) busyDone.TrySetResult(); });

        // First render call happens synchronously, with the unfiltered (but sorted) list.
        Assert.HasCount(1, renders);
        Assert.HasCount(2, renders[0]);

        filterGate.SetResult(all.Where(r => r.FullPath == @"C:\a").ToList());
        await busyDone.Task;

        Assert.HasCount(2, renders);
        Assert.HasCount(1, renders[1]);
        Assert.AreEqual(@"C:\a", renders[1][0].FullPath);
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

        // Only the synchronous unfiltered render happened; the stale filtered render was skipped.
        Assert.HasCount(1, renders);
    }
}
