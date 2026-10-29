using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Helpers;

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
        public long Revision;
        /// <summary>Restored from disk rather than scanned in this run -- see DiskGraceMs.</summary>
        public bool FromDisk;
    }

    // Sized to hold the whole warm-up set: 0-9 and a-z across every drive (36 x drives). A smaller
    // capacity made the warm-up evict its own earlier entries, so nothing it pre-answered ever hit
    // (measured: 0 hits out of 4 drives).
    private const int Capacity = 256;

    // Correctness is carried by the index revision, not by time: a cached page records the revision it was
    // scanned from and is refused as soon as the index moves, so a file the user just created can never be
    // answered from a page that predates it. This window only survives the case where the revision moved
    // DURING a burst of typing, where refusing every page would throw away the whole cache between two
    // keystrokes. It is deliberately short -- long enough to bridge consecutive keystrokes, short enough
    // that a new file shows up almost at once.
    private const long RecentGraceMs = 2_000;

    // How long a page restored from disk may be served. The revision rule below is useless for one of those:
    // a saved page carries the revision its process had when it was saved, and revision restarts from zero
    // every launch, so the two are never comparable -- measured, a restored page was refused within two
    // seconds of loading, which is why the disk cache appeared to do nothing. These are retired by age alone.
    // Ten minutes is a compromise: long enough that a session starts warm, short enough that a file created
    // while the service was down cannot be hidden for long -- and the in-memory revision rule still retires
    // any page the index invalidates during this run.
    private const long DiskGraceMs = 10 * 60_000;

    // A stored page is a prefix of its scan, never the whole drive: the full window asks for
    // int.MaxValue, and keeping that answer would pin an unbounded amount of memory per entry. A
    // prefix of this size still covers the quick window (204 candidates) and any smaller request.
    private const int MaxRows = 512;

    private readonly Dictionary<string, Entry> _pages = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public static string KeyFor(string query, string? directoryFilter, string? fileNameFilter, SearchCategoryFilter category)
        => string.Concat(query, "\u0001", directoryFilter ?? string.Empty, "\u0001", fileNameFilter ?? string.Empty, "\u0001", ((byte)category).ToString(System.Globalization.CultureInfo.InvariantCulture));

    public bool TryReplay(string key, int limit, Action<SearchResult> onResult, CancellationToken token, long revision)
    {
        List<SearchResult> page;
        lock (_gate)
        {
            if (!_pages.TryGetValue(key, out var entry))
                return false;

            // Two retirement rules, because the two kinds of page have nothing in common. A page scanned
            // in this run belongs to the index revision it came from and is refused the moment that moves,
            // which is what stops it from answering with rows that no longer exist; the short grace covers
            // only the revision changing during a typing burst. A page restored from disk cannot use
            // revision at all, so it is retired by age -- see DiskGraceMs.
            if (entry.FromDisk)
            {
                if (Environment.TickCount64 - entry.StoredAt > DiskGraceMs)
                    return false;
            }
            else if (entry.Revision != revision && Environment.TickCount64 - entry.StoredAt > RecentGraceMs)
                return false;

            // A page shorter than the request is a truncated answer: replaying it would end the stream
            // at its last row and hide every later match. That is how a 204-row quick page used to
            // answer the full window (which asks for int.MaxValue) and stop at 204. Fall through to a
            // real scan instead, which also stores a page big enough for this request.
            if (entry.Rows.Count < limit)
                return false;

            Lertaro.Core.Services.Search.SearchPipelineProbe.Write("idx.cache-hit", entry.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
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

    public void Store(string key, List<SearchResult> rows, long revision)
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

            _pages[key] = new Entry { Rows = rows, LastUsed = Environment.TickCount64, StoredAt = Environment.TickCount64, Revision = revision };
        }
    }

    // Test seam: the allocation tests measure a cold scan on both sides of a comparison, and a page
    // stored by the warm-up call would answer one side with a replay instead of that scan.
    internal void Clear()
    {
        lock (_gate)
            _pages.Clear();
    }

    /// <summary>Copies the pages out for persistence. The row lists are shared, not cloned.</summary>
    internal List<KeyValuePair<string, List<SearchResult>>> Snapshot()
    {
        lock (_gate)
        {
            var copy = new List<KeyValuePair<string, List<SearchResult>>>(_pages.Count);
            foreach (var pair in _pages)
                copy.Add(new KeyValuePair<string, List<SearchResult>>(pair.Key, pair.Value.Rows));
            return copy;
        }
    }

    /// <summary>
    /// Installs pages restored from disk, stamped with the current revision so the usual staleness rule
    /// (a page is refused once the index revision moves) still retires them.
    /// </summary>
    internal void Import(IReadOnlyDictionary<string, List<SearchResult>> pages, long revision)
    {
        lock (_gate)
        {
            foreach (var pair in pages)
            {
                if (pair.Value.Count == 0)
                    continue;
                if (_pages.Count >= Capacity)
                    EvictOldestLocked();
                _pages[pair.Key] = new Entry
                {
                    Rows = pair.Value,
                    LastUsed = Environment.TickCount64,
                    StoredAt = Environment.TickCount64,
                    Revision = revision,
                    FromDisk = true
                };
            }
        }
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
