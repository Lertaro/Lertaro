using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.IndexV2.Search;

// Rows of a query this drive already answered, keyed by the query and its filters. One cache belongs to
// one LiveIndex: a page is only a valid answer for the snapshot/delta pair it was scanned from, so a
// process-wide cache let a page stored for one drive -- or for an earlier index opened under the same
// drive letter -- answer another's query with rows that never existed there. Nothing is persisted;
// disposing the index drops the cache with it.
internal sealed class QueryPageCache
{
    private sealed class Entry
    {
        public List<SearchResult> Rows = new();
        public long LastUsed;
        public long StoredAt;
    }

    // Sized to hold the whole warm-up set: 0-9 and a-z across every drive (36 x drives). A smaller
    // capacity made the warm-up evict its own earlier entries, so nothing it pre-answered ever hit
    // (measured: 0 hits out of 4 drives).
    private const int Capacity = 256;

    // How long a cached page may outlive an index change. Short enough that a file the user just
    // created shows up on the next search, long enough to cover the pause between two searches a
    // person actually makes.
    private const long RecentGraceMs = 60_000;

    // A stored page is a prefix of its scan, never the whole drive: the full window asks for
    // int.MaxValue, and keeping that answer would pin an unbounded amount of memory per entry. A
    // prefix of this size still covers the quick window (204 candidates) and any smaller request.
    private const int MaxRows = 512;

    private readonly Dictionary<string, Entry> _pages = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public static string KeyFor(string query, string? directoryFilter, string? fileNameFilter)
        => string.Concat(query, "\u0001", directoryFilter ?? string.Empty, "\u0001", fileNameFilter ?? string.Empty);

    public bool TryReplay(string key, int limit, Action<SearchResult> onResult, CancellationToken token)
    {
        List<SearchResult> page;
        lock (_gate)
        {
            if (!_pages.TryGetValue(key, out var entry))
                return false;

            // Time, not revision, is what retires a page. The desktop changes files constantly, so
            // the revision moved between almost every pair of searches and an exact-match rule left
            // 2 of 4 drives hitting while the other 2 always missed, which put the slowest miss
            // back on the critical path. A page this young is served as-is; older than the window
            // it is rebuilt, which is what lets a just-created file show up a minute later.
            if (Environment.TickCount64 - entry.StoredAt > RecentGraceMs)
                return false;

            // A page shorter than the request is a truncated answer: replaying it would end the stream
            // at its last row and hide every later match. That is how a 204-row quick page used to
            // answer the full window (which asks for int.MaxValue) and stop at 204. Fall through to a
            // real scan instead, which also stores a page big enough for this request.
            if (entry.Rows.Count < limit)
                return false;

            Lertaro.Core.SearchIndex.PerfTrace.Mark("idx.cacheHit", Lertaro.Core.SearchIndex.PerfTrace.Begin(), entry.Rows.Count);
            entry.LastUsed = Environment.TickCount64;
            // Copy while the entry is pinned, emit after unlocking: onResult can block on the pipe's
            // bounded channel, and holding the gate across it would serialize every other replay and
            // store behind one slow client.
            page = entry.Rows.Count == limit ? entry.Rows : entry.Rows.GetRange(0, limit);
        }

        foreach (var row in page)
        {
            token.ThrowIfCancellationRequested();
            onResult(row);
        }

        return true;
    }

    public void Store(string key, List<SearchResult> rows)
    {
        if (rows.Count == 0)
            return;

        // A full-window search asks for int.MaxValue and can return the whole drive; keeping that page
        // would pin an unbounded amount of memory per entry. The prefix stays a valid answer for any
        // request it can cover, because TryReplay refuses a page shorter than the request.
        if (rows.Count > MaxRows)
            rows = rows.GetRange(0, MaxRows);

        lock (_gate)
        {
            // Deliberately no _pages.Clear() here: a mutation of this index does not invalidate the
            // other entries (the age check already bounds them), and clearing on every refresh would
            // throw away pages the warm-up had just stored.
            if (_pages.Count >= Capacity)
                EvictOldestLocked();

            _pages[key] = new Entry { Rows = rows, LastUsed = Environment.TickCount64, StoredAt = Environment.TickCount64 };
        }
    }

    // Test seam: the allocation tests measure a cold scan on both sides of a comparison, and a page
    // stored by the warm-up call would answer one side with a replay instead of that scan.
    internal void Clear()
    {
        lock (_gate)
            _pages.Clear();
    }

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
}
