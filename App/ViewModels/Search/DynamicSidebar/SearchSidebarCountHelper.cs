namespace Lertaro.App.ViewModels.Search.DynamicSidebar;

// Owns the streamed sidebar-count state so SearchViewModel remains focused on query orchestration.
// Counts are published at most once per short interval; predicates still run for every newly arrived
// row, but WPF does not receive one property notification per row in a large search.
internal sealed class SearchSidebarCountHelper
{
    private readonly IReadOnlyList<DynamicSidebarItemViewModel> _items;
    private readonly SidebarFilterCountAccumulator _accumulator;
    private DateTime _lastPublish = DateTime.MinValue;

    public SearchSidebarCountHelper(IReadOnlyList<DynamicSidebarItemViewModel> items)
    {
        _items = items;
        _accumulator = new SidebarFilterCountAccumulator(items.Select(item => item.MatchPredicate).ToList());
    }

    public void Reset()
    {
        _accumulator.Reset();
        _lastPublish = DateTime.MinValue;
        foreach (var item in _items)
            item.ClearCount();
    }

    public void Update(IReadOnlyList<AppSearchResult> batch, bool final)
    {
        _accumulator.AddBatch(batch);
        if (!final && DateTime.UtcNow - _lastPublish < TimeSpan.FromMilliseconds(120))
            return;

        var counts = _accumulator.Counts;
        for (var index = 0; index < _items.Count; index++)
            _items[index].SetCount(counts[index]);
        _lastPublish = DateTime.UtcNow;
    }
}
