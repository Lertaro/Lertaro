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
        Array.Empty<SearchResultMapper.RankedCandidate>());

    private readonly SearchResultMapper.RankedCandidate[] _rows;
    private readonly RankKey[] _keys;
    private readonly RankContext? _context;

    internal RankingPassTrace(
        int preMergeCount,
        SearchResultMapper.RankedCandidate[] rows,
        RankKey[] keys,
        RankContext? context,
        IReadOnlyList<SearchResultMapper.RankedCandidate> mergedAway)
    {
        PreMergeCount = preMergeCount;
        _rows = rows;
        _keys = keys;
        _context = context;
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
            _rows[index], _keys[index], index + 1, _context, providerState);
        return true;
    }

    internal RankingLogRow DescribeAt(int index, RankingProviderState providerState) =>
        RankedCandidateOrdering.Describe(
            _rows[index], _keys[index], index + 1, _context, providerState);

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
