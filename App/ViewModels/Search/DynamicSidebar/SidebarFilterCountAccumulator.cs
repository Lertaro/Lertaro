using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.ViewModels.Search.DynamicSidebar;

// Kept separate from the WPF view models so the streamed counting rules remain cheap to exercise
// without constructing a dispatcher-bound SearchViewModel.
internal sealed class SidebarFilterCountAccumulator
{
    private readonly IReadOnlyList<Func<ISearchResult, bool>> _predicates;
    private readonly int[] _counts;

    public SidebarFilterCountAccumulator(IReadOnlyList<Func<ISearchResult, bool>> predicates)
    {
        _predicates = predicates;
        _counts = new int[predicates.Count];
    }

    public IReadOnlyList<int> Counts => _counts;

    public void Reset() => Array.Clear(_counts, 0, _counts.Length);

    public void AddBatch(IReadOnlyList<ISearchResult> results)
    {
        foreach (var result in results)
        {
            for (var index = 0; index < _predicates.Count; index++)
            {
                if (_predicates[index](result))
                    _counts[index]++;
            }
        }
    }
}
