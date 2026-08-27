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

    private void PublishCounts()
    {
        var selectedPredicates = _groups.Select(group =>
            (IReadOnlyList<Func<ISearchResult, bool>>)group.Items
                .Where(item => item.IsSelected)
                .Select(item => item.MatchPredicate)
                .ToList()).ToList();
        var counts = _accumulator.Calculate(selectedPredicates);
        for (var index = 0; index < _items.Count; index++)
            _items[index].SetCount(counts[index]);
        _lastPublish = DateTime.UtcNow;
    }
}
