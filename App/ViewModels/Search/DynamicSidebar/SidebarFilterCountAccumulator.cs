using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.ViewModels.Search.DynamicSidebar;

// Kept separate from the WPF view models so the streamed counting rules remain cheap to exercise
// without constructing a dispatcher-bound SearchViewModel.
internal sealed class SidebarFilterCountAccumulator
{
    private readonly IReadOnlyList<IReadOnlyList<Func<ISearchResult, bool>>> _predicatesByGroup;
    private readonly int[] _counts;
    private readonly List<ISearchResult> _results = new();

    public SidebarFilterCountAccumulator(IReadOnlyList<IReadOnlyList<Func<ISearchResult, bool>>> predicatesByGroup)
    {
        _predicatesByGroup = predicatesByGroup;
        _counts = new int[predicatesByGroup.Sum(group => group.Count)];
    }

    public IReadOnlyList<int> Counts => _counts;

    public void Reset()
    {
        _results.Clear();
        Array.Clear(_counts, 0, _counts.Length);
    }

    public void AddBatch(IReadOnlyList<ISearchResult> results) => _results.AddRange(results);

    public void ReplaceResults(IReadOnlyList<ISearchResult> results)
    {
        _results.Clear();
        _results.AddRange(results);
    }

    public IReadOnlyList<int> Calculate(IReadOnlyList<IReadOnlyList<Func<ISearchResult, bool>>> selectedPredicatesByGroup)
    {
        Array.Clear(_counts, 0, _counts.Length);
        var groupMatches = new bool[_predicatesByGroup.Count];
        var groupOffsets = new int[_predicatesByGroup.Count];
        var offset = 0;
        for (var groupIndex = 0; groupIndex < _predicatesByGroup.Count; groupIndex++)
        {
            groupOffsets[groupIndex] = offset;
            offset += _predicatesByGroup[groupIndex].Count;
        }

        foreach (var result in _results)
        {
            for (var groupIndex = 0; groupIndex < _predicatesByGroup.Count; groupIndex++)
            {
                var selectedPredicates = selectedPredicatesByGroup[groupIndex];
                groupMatches[groupIndex] = selectedPredicates.Count == 0
                    || selectedPredicates.Any(predicate => predicate(result));
            }

            for (var targetGroupIndex = 0; targetGroupIndex < _predicatesByGroup.Count; targetGroupIndex++)
            {
                var otherGroupsMatch = true;
                for (var groupIndex = 0; groupIndex < _predicatesByGroup.Count; groupIndex++)
                {
                    if (groupIndex != targetGroupIndex && !groupMatches[groupIndex])
                    {
                        otherGroupsMatch = false;
                        break;
                    }
                }
                if (!otherGroupsMatch)
                    continue;

                var targetPredicates = _predicatesByGroup[targetGroupIndex];
                for (var itemIndex = 0; itemIndex < targetPredicates.Count; itemIndex++)
                {
                    if (targetPredicates[itemIndex](result))
                        _counts[groupOffsets[targetGroupIndex] + itemIndex]++;
                }
            }
        }

        return _counts;
    }
}
