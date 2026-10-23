using Lertaro.Core.SearchIndex;

namespace Lertaro.App.ViewModels.Search.Mapping;

/// <summary>Computed keys and merge data retained until one mapper pass reaches the UI.</summary>
internal sealed class RankingPassTrace
{
    internal static readonly RankingPassTrace Empty = new(
        0,
        Array.Empty<SearchResultMapper.RankedCandidate>(),
        Array.Empty<RankKey>(),
        null,
        new Dictionary<string, HistoryTier>(StringComparer.OrdinalIgnoreCase),
        Array.Empty<SearchResultMapper.RankedCandidate>());

    private readonly SearchResultMapper.RankedCandidate[] _rows;
    private readonly RankKey[] _keys;
    private readonly RankContext? _context;
    private readonly IReadOnlyDictionary<string, HistoryTier> _budget;
    private readonly int[] _shadowV3Ranks;

    internal RankingPassTrace(
        int preMergeCount,
        SearchResultMapper.RankedCandidate[] rows,
        RankKey[] keys,
        RankContext? context,
        IReadOnlyDictionary<string, HistoryTier> budget,
        IReadOnlyList<SearchResultMapper.RankedCandidate> mergedAway)
    {
        PreMergeCount = preMergeCount;
        _rows = rows;
        _keys = keys;
        _context = context;
        _budget = budget;
        _shadowV3Ranks = RankedCandidateOrdering.ComputeShadowV3Ranks(rows, context, budget);
        MergedAway = mergedAway;
    }

    internal int PreMergeCount { get; }
    internal int PostMergeCount => _rows.Length;
    internal IReadOnlyList<SearchResultMapper.RankedCandidate> Rows => _rows;
    internal IReadOnlyList<SearchResultMapper.RankedCandidate> MergedAway { get; }

    internal bool TryDescribe(AppSearchResult result, RankingProviderState providerState, out RankingLogRow row)
    {
        var index = IndexOf(result);
        if (index < 0)
        {
            row = default;
            return false;
        }

        row = RankedCandidateOrdering.Describe(
            _rows[index], _keys[index], index + 1, _context, _budget, providerState, _shadowV3Ranks[index]);
        return true;
    }

    internal RankingLogRow DescribeAt(int index, RankingProviderState providerState) =>
        RankedCandidateOrdering.Describe(
            _rows[index], _keys[index], index + 1, _context, _budget, providerState, _shadowV3Ranks[index]);

    internal RankingLogRow DescribeShadow(
        SearchResultMapper.RankedCandidate candidate,
        RankingProviderState providerState) =>
        RankedCandidateOrdering.DescribeShadow(candidate, _context, providerState);

    internal int IndexOf(AppSearchResult result)
    {
        for (var i = 0; i < _rows.Length; i++)
        {
            if (ReferenceEquals(_rows[i].Result, result))
                return i;
        }

        // Token providers may rebuild a row; path identity is a diagnostic fallback.
        for (var i = 0; i < _rows.Length; i++)
        {
            if (string.Equals(_rows[i].Result.FullPath, result.FullPath, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
