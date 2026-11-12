using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Core.IndexV2.Search;

// Per-drive cache for completed quick-search pages. The category is part of the key, so switching tabs
// can reuse a full category answer without filtering the current display page. A page is valid only for
// the exact index revision it was scanned from; a USN/watcher batch therefore invalidates it immediately.
internal sealed class QueryPageCache
{
    private sealed class Entry
    {
        public required List<SearchResult> Rows { get; init; }
        public required bool Complete { get; init; }
        public required bool Truncated { get; init; }
        public required long Revision { get; init; }
        public long LastUsed { get; set; }
    }

    private const int Capacity = 128;
    internal const int MaxRows = 1024;
    // 128 full pages would otherwise pin 131k rows per drive; cap the retained rows as well so a
    // burst of distinct queries cannot keep the whole theoretical maximum alive between idle trims.
    internal const int MaxTotalRows = 32 * 1024;
    private const long IdleExpiryMs = 30_000;

    private readonly Dictionary<string, Entry> _pages = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _replayCount;

    internal int RetainedEntryCount
    {
        get
        {
            lock (_gate)
                return _pages.Count;
        }
    }

    internal int RetainedRowCount
    {
        get
        {
            lock (_gate)
                return _pages.Values.Sum(entry => entry.Rows.Count);
        }
    }

    internal long ReplayCount => Interlocked.Read(ref _replayCount);

    internal void Clear()
    {
        lock (_gate)
        {
            _pages.Clear();
        }
    }

    internal static string KeyFor(string query, string? directoryFilter, string? fileNameFilter,
        SearchCategoryFilter category)
    {
        // The same text means different things in exact/fuzzy mode, under either operator precedence,
        // and with alias providers disabled. Revision alone cannot distinguish those requests.
        var disabledAliases = SearchContext.DisabledAliasIds;
        var disabledKey = disabledAliases == null
            ? string.Empty
            : string.Join(',', disabledAliases.Order());
        return string.Concat(
            query, "\u0001",
            directoryFilter ?? string.Empty, "\u0001",
            fileNameFilter ?? string.Empty, "\u0001",
            ((byte)category).ToString(System.Globalization.CultureInfo.InvariantCulture), "\u0001",
            SearchContext.FuzzyMatchEnabled ? "fuzzy" : "exact", "\u0001",
            SearchContext.AndFirstPrecedence ? "and-first" : "or-first", "\u0001",
            disabledKey, "\u0001",
            AliasProviderRegistry.QueryRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    internal bool TryReplay(string key, int limit, Action<SearchResult> onResult,
        CancellationToken token, long revision)
    {
        List<SearchResult> page;
        lock (_gate)
        {
            if (!_pages.TryGetValue(key, out var entry))
                return false;

            var now = Environment.TickCount64;
            if (now - entry.LastUsed > IdleExpiryMs)
            {
                _pages.Remove(key);
                return false;
            }

            if (entry.Revision != revision
                || (entry.Truncated && limit > MaxRows)
                || (entry.Rows.Count < limit && !entry.Complete))
            {
                return false;
            }

            entry.LastUsed = now;
            page = entry.Rows.Count <= limit ? entry.Rows : entry.Rows.GetRange(0, limit);
        }

        foreach (var row in page)
        {
            token.ThrowIfCancellationRequested();
            onResult(Clone(row));
        }
        Interlocked.Increment(ref _replayCount);
        return true;
    }

    internal void Store(string key, List<SearchResult> rows, int requestLimit, long revision,
        int totalRows = -1, bool clone = true)
    {
        if (totalRows < 0)
            totalRows = rows.Count;

        // IndexV2Searcher.Capture already detaches every row before the caller sees it, so the
        // production path passes clone:false and transfers ownership instead of copying twice.
        if (clone)
        {
            var cloned = new List<SearchResult>(Math.Min(rows.Count, MaxRows));
            foreach (var row in rows.Take(MaxRows))
                cloned.Add(Clone(row));
            rows = cloned;
        }
        else if (rows.Count > MaxRows)
        {
            rows = rows.GetRange(0, MaxRows);
        }

        var entry = new Entry
        {
            Rows = rows,
            Complete = totalRows < requestLimit,
            Truncated = totalRows > MaxRows,
            Revision = revision,
            LastUsed = Environment.TickCount64,
        };

        lock (_gate)
        {
            RemoveExpiredLocked(Environment.TickCount64);
            if (_pages.Count >= Capacity && !_pages.ContainsKey(key))
                EvictOldestLocked();
            _pages[key] = entry;
            TrimToRowBudgetLocked();
        }
    }

    private void TrimToRowBudgetLocked()
    {
        while (_pages.Count > 1 && _pages.Values.Sum(entry => entry.Rows.Count) > MaxTotalRows)
            EvictOldestLocked();
    }

    internal static SearchResult Clone(SearchResult source) => new()
    {
        Name = source.Name,
        Path = source.Path,
        IsDir = source.IsDir,
        Drive = source.Drive,
        Attributes = source.Attributes,
        RankSortKey = source.RankSortKey,
        RankScore = source.RankScore,
        RankFlags = source.RankFlags,
        HistoryScoreX100 = source.HistoryScoreX100,
        RankOrderKey = source.RankOrderKey,
        HistoryTie = source.HistoryTie,
        Metadata = source.Metadata,
    };

    private void EvictOldestLocked()
    {
        string? oldestKey = null;
        var oldest = long.MaxValue;
        foreach (var pair in _pages)
        {
            if (pair.Value.LastUsed >= oldest)
                continue;
            oldest = pair.Value.LastUsed;
            oldestKey = pair.Key;
        }

        if (oldestKey != null)
            _pages.Remove(oldestKey);
    }

    private void RemoveExpiredLocked(long now)
    {
        List<string>? expired = null;
        foreach (var pair in _pages)
        {
            if (now - pair.Value.LastUsed <= IdleExpiryMs)
                continue;
            (expired ??= []).Add(pair.Key);
        }

        if (expired == null)
            return;
        foreach (var key in expired)
            _pages.Remove(key);
    }
}
