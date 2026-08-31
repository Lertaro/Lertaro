using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.ViewModels.Search.DynamicSidebar;

// Applies the active sidebar filter predicates (the host adapts each item's synchronous matcher to
// this batch shape) on top of an already-sorted result list. Renders immediately with the sorted-but-unfiltered list, then
// swaps in the filtered list once the predicates resolve -- discarding a stale resolution if a
// newer results set or filter selection has since taken over.
internal sealed class DynamicFilterCoordinator
{
    private List<Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>>>? _pendingFilters;

    public void Apply(
        List<AppSearchResult> allResults,
        List<Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>>> activeFilters,
        Func<IEnumerable<AppSearchResult>, List<AppSearchResult>> sort,
        Func<List<AppSearchResult>> currentResults,
        Action<List<AppSearchResult>> render,
        Action<bool> setBusy)
    {
        var sorted = sort(allResults);

        if (activeFilters.Count == 0)
        {
            _pendingFilters = null;
            setBusy(false);
            render(sorted);
            return;
        }

        render(sorted);
        _pendingFilters = activeFilters;
        setBusy(true);
        _ = ApplyAsync(allResults, activeFilters, sort, currentResults, render, setBusy);
    }

    private async Task ApplyAsync(
        List<AppSearchResult> resultsSnapshot,
        List<Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>>> filtersSnapshot,
        Func<IEnumerable<AppSearchResult>, List<AppSearchResult>> sort,
        Func<List<AppSearchResult>> currentResults,
        Action<List<AppSearchResult>> render,
        Action<bool> setBusy)
    {
        try
        {
            IReadOnlyList<ISearchResult> current = resultsSnapshot;
            foreach (var filter in filtersSnapshot)
                current = await filter(current);

            if (!ReferenceEquals(currentResults(), resultsSnapshot) || !ReferenceEquals(_pendingFilters, filtersSnapshot))
                return;

            render(sort(current.Cast<AppSearchResult>()));
        }
        finally
        {
            // A newer Apply() call already reassigned _pendingFilters to ITS OWN filter list -- that
            // call owns the busy indicator now, so this (superseded) call must not clear it out from
            // under it.
            if (ReferenceEquals(_pendingFilters, filtersSnapshot))
                setBusy(false);
        }
    }
}
