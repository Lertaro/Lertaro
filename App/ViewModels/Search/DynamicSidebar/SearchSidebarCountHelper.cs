using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.ViewModels.Search.DynamicSidebar;

// Owns the streamed sidebar-count state so SearchViewModel remains focused on query orchestration.
// Counts are published at most once per short interval; predicates still run for every newly arrived
// row, but WPF does not receive one property notification per row in a large search.
internal sealed class SearchSidebarCountHelper
{
    private readonly IReadOnlyList<DynamicSidebarGroupViewModel> _groups;
    private readonly IReadOnlyList<DynamicSidebarItemViewModel> _items;
    private readonly SidebarFilterCountAccumulator _accumulator;
    private DateTime _lastPublish = DateTime.MinValue;
    private bool _hasResults;

    public SearchSidebarCountHelper(IReadOnlyList<DynamicSidebarGroupViewModel> groups)
    {
        _groups = groups;
        _items = groups.SelectMany(group => group.Items).ToList();
        _accumulator = new SidebarFilterCountAccumulator(
            groups.Select(group =>
                (IReadOnlyList<Func<ISearchResult, bool>>)group.Items.Select(item => item.MatchPredicate).ToList())
            .ToList());
        SearchPerf.Log($"[sidebar] groups={groups.Count} items={_items.Count} predicates={_accumulator.PredicateCount}");
    }

    public void Reset()
    {
        _accumulator.Reset();
        _lastPublish = DateTime.MinValue;
        _hasResults = false;
        foreach (var item in _items)
            item.ClearCount();
    }

    public void Update(IReadOnlyList<AppSearchResult> batch, bool final)
    {
        _accumulator.AddBatch(batch);
        _hasResults = true;
        if (!final && DateTime.UtcNow - _lastPublish < TimeSpan.FromMilliseconds(120))
            return;

        PublishCounts();
    }

    public void Recalculate()
    {
        if (_hasResults)
            PublishCounts();
    }

    public void Replace(IReadOnlyList<AppSearchResult> results)
    {
        var perfStart = SearchPerf.Start();
        _accumulator.ReplaceResults(results);
        var copyMs = SearchPerf.Ms(perfStart);
        _hasResults = true;
        PublishCounts($"rows={results.Count} copy={copyMs:F0}ms");
    }

    private void PublishCounts(string? context = null)
    {
        var selectStart = SearchPerf.Start();
        var selectedPredicates = _groups.Select(group =>
            (IReadOnlyList<Func<ISearchResult, bool>>)group.Items
                .Where(item => item.IsSelected)
                .Select(item => item.MatchPredicate)
                .ToList()).ToList();
        var selectMs = SearchPerf.Ms(selectStart);
        var calcStart = SearchPerf.Start();
        var gcBefore = GC.GetTotalPauseDuration();
        var gen2Before = GC.CollectionCount(2);
        var counts = _accumulator.Calculate(selectedPredicates);
        var calcMs = SearchPerf.Ms(calcStart);
        var gcMs = (GC.GetTotalPauseDuration() - gcBefore).TotalMilliseconds;
        var setStart = SearchPerf.Start();
        for (var index = 0; index < _items.Count; index++)
            _items[index].SetCount(counts[index]);
        SearchPerf.Log($"[sidebar.publish] {context} predicates={_accumulator.PredicateCount} items={_items.Count} select={selectMs:F0}ms calc={calcMs:F0}ms gc={gcMs:F0}ms gen2=+{GC.CollectionCount(2) - gen2Before} set={SearchPerf.Ms(setStart):F0}ms");
        _lastPublish = DateTime.UtcNow;
    }
}
