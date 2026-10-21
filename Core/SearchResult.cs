using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Core;

public class SearchResult : ISearchResult
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    /// <summary>Plugin SDK contract alias for <see cref="Path"/>.</summary>
    public string FullPath => Path;

    /// <summary>Plugin SDK contract: the directory a result lives in (itself for folders).</summary>
    public string ContextDirectory => IsDir ? Path : System.IO.Path.GetDirectoryName(Path) ?? Drive + ":\\";

    public bool IsDir { get; set; }
    public string Drive { get; set; } = string.Empty;

    public bool IsApplication => false;

    public FileAttributes Attributes { get; set; }
    internal ulong RankSortKey { get; set; }

    // Filled once by SearchResultRanking.Decorate; the full window compares these instead of re-deriving
    // rule verdicts and history lookups on every comparison.
    internal int RankScore { get; set; }

    // Bits 0-1 section, bit 2 strong, bits 3-4 history tier. A byte rather than three bools so a
    // hundred thousand rows cost six bytes instead of sixteen.
    internal byte RankFlags { get; set; }

    internal int HistoryScoreX100 { get; set; }

    // Populated from the index for every result that comes from one (local or network/WSL) -- lets
    // SearchService merge GetRecentFiles' local and network/WSL result sets by actual recency instead of
    // just concatenating two already-sorted-but-incomparable lists, lets the sidebar Size/Date filters and
    // the Size column run synchronously off already-known data instead of a per-application IPC round
    // trip (see FileSizeFilterProvider/DateModifiedFilterProvider), and lets the UI show a modified date
    // without re-touching the filesystem (see AppSearchResult.DateModified). Default for results that
    // don't come from an index (plugin results, etc.) or genuinely have no recorded metadata.
    public FileMetadata Metadata { get; set; }
}

public sealed class SearchResultRankComparer : IComparer<SearchResult>
{
    public static readonly SearchResultRankComparer Instance = new(new Dictionary<string, int>());

    private readonly IReadOnlyDictionary<string, int> _historySnapshot;
    private readonly IReadOnlyDictionary<string, int>? _usageCounts;
    private readonly IReadOnlySet<string>? _favoritePaths;
    private readonly Func<string, bool, IndexRuleEffect>? _priorityResolver;
    private readonly Dictionary<string, IndexRuleEffect>? _priorityCache;

    public SearchResultRankComparer(
        IReadOnlyDictionary<string, int> historySnapshot,
        IReadOnlyDictionary<string, int>? usageCounts = null,
        IReadOnlySet<string>? favoritePaths = null,
        Func<string, bool, IndexRuleEffect>? priorityResolver = null)
    {
        _historySnapshot = historySnapshot;
        _usageCounts = usageCounts;
        _favoritePaths = favoritePaths;
        _priorityResolver = priorityResolver;
        _priorityCache = priorityResolver != null ? new Dictionary<string, IndexRuleEffect>(StringComparer.OrdinalIgnoreCase) : null;
    }

    // Smallest wins in the ascending sort: high first, low last.
    public static int PriorityRank(IndexRuleEffect effect) => effect switch
    {
        IndexRuleEffect.High => 0,
        IndexRuleEffect.Low => 2,
        _ => 1,
    };

    private IndexRuleEffect ResolvePriority(string path, bool isDirectory)
    {
        if (_priorityResolver == null)
            return IndexRuleEffect.Normal;
        if (_priorityCache!.TryGetValue(path, out var cached))
            return cached;
        var priority = _priorityResolver(path, isDirectory);
        _priorityCache[path] = priority;
        return priority;
    }

    // Histories with five or more opens get band 0; one to four opens band 1; no history band 2. A
    // favorite is a separate band before this one. The band is compared AFTER every match key, so neither
    // overrules a better textual match. This is the same sequence RankedCandidateOrdering uses.
    public static int HistoryBand(int usageCount) => usageCount >= 5 ? 0 : usageCount >= 1 ? 1 : 2;

    private static string NormalizeForLookup(string path)
    {
        if (path.Length > 3 && path[^1] == '\\')
            return path.TrimEnd('\\');
        return path;
    }

    public int Compare(SearchResult? left, SearchResult? right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left == null)
            return 1;
        if (right == null)
            return -1;

        var leftPath = NormalizeForLookup(left.Path);
        var rightPath = NormalizeForLookup(right.Path);

        int compare;
        // Result type is grouped above this comparer; within one type, a low-priority folder (build
        // output, Windows.old) must not outrank a normal hit merely by matching the text better.
        if (_priorityResolver != null)
        {
            compare = PriorityRank(ResolvePriority(leftPath, left.IsDir))
                .CompareTo(PriorityRank(ResolvePriority(rightPath, right.IsDir)));
            if (compare != 0)
                return compare;
        }

        // Match quality first: contiguity, shape, left-contiguous (tie-break), start, coverage and tier
        // are packed into the high 32 bits by SearchResultRelevance. The low bits are the engine's own
        // fzf tie-breakers and are deliberately kept below every window-level key.
        compare = (left.RankSortKey >> 32).CompareTo(right.RankSortKey >> 32);
        if (compare != 0)
            return compare;

        var leftCount = _usageCounts != null && _usageCounts.TryGetValue(leftPath, out var lc) ? lc : 0;
        var rightCount = _usageCounts != null && _usageCounts.TryGetValue(rightPath, out var rc) ? rc : 0;

        // A favorite is an explicit user pick and sits before history, exactly as in the quick ladder.
        var leftFavorite = _favoritePaths?.Contains(leftPath) == true ? 0 : 1;
        var rightFavorite = _favoritePaths?.Contains(rightPath) == true ? 0 : 1;
        compare = leftFavorite.CompareTo(rightFavorite);
        if (compare != 0)
            return compare;

        // Older callers only hand over the priority map; a path present there is weak (1-4) history.
        if (leftCount == 0 && _historySnapshot.ContainsKey(leftPath))
            leftCount = 1;
        if (rightCount == 0 && _historySnapshot.ContainsKey(rightPath))
            rightCount = 1;

        compare = HistoryBand(leftCount).CompareTo(HistoryBand(rightCount));
        if (compare != 0)
            return compare;

        var leftHistoryPriority = _historySnapshot.TryGetValue(leftPath, out var lp) ? lp : int.MaxValue;
        var rightHistoryPriority = _historySnapshot.TryGetValue(rightPath, out var rp) ? rp : int.MaxValue;
        compare = leftHistoryPriority.CompareTo(rightHistoryPriority);
        if (compare != 0)
            return compare;

        compare = left.IsDir.CompareTo(right.IsDir);
        if (compare != 0)
            return compare;

        compare = SearchNameOrderComparer.Instance.Compare(left.Name, right.Name);
        if (compare != 0)
            return compare;

        compare = left.Name.Length.CompareTo(right.Name.Length);
        if (compare != 0)
            return compare;

        compare = leftPath.Length.CompareTo(rightPath.Length);
        if (compare != 0)
            return compare;

        compare = string.Compare(left.Drive, right.Drive, StringComparison.OrdinalIgnoreCase);
        if (compare != 0)
            return compare;

        compare = string.Compare(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
        if (compare != 0)
            return compare;

        return left.RankSortKey.CompareTo(right.RankSortKey);
    }
}
