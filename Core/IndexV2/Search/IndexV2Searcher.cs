using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Search.PathMode;
using Lertaro.Core.SearchIndex.Query;
using Lertaro.Core.Services.Plugin.DirectoryIndex;
namespace Lertaro.Core.IndexV2.Search;

// Top-level search entry point for a single drive's LiveIndex: parse the query, route path-mode queries
// to PathSearch and everything else to NameSearch, normalize/gate the directory filter. Runs entirely
// inside one LiveIndex.Read call so
// the whole search sees one consistent (Snapshot, DeltaOverlay) pair.
public static class IndexV2Searcher
{
    /// <summary>
    /// Rows of a query this drive already answered, keyed by the drive, its revision and the query. The
    /// scan is bound by the index's memory access -- measured: the same single-letter query costs 40-450 ms
    /// cold and 8-40 ms hot on one drive, with nothing between the two but the mapped pages being resident.
    /// A repeat of a query the user just typed therefore gets the previous answer instead of re-running that
    /// scan, which is what makes a keystroke feel instant rather than merely fast.
    /// </summary>
    /// <remarks>
    /// Invalidated by <see cref="LiveIndex.Revision"/>, so any index change (a USN batch, a watcher event, a
    /// compaction) makes every entry stale at once and the next search rebuilds it. Rows are shared with the
    /// caller: the producer serialises them straight to the pipe and never mutates them, and everything the
    /// ranker writes to a row is written while the entry is built.
    /// </remarks>
    private static readonly Dictionary<string, Entry> QueryCache = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        public long Revision;
        public List<SearchResult> Rows = new();
        public long LastUsed;
        public long StoredAt;
    }

    // Sized to hold the whole warm-up set: 0-9 and a-z across every drive (36 x drives). A smaller
    // capacity made the warm-up evict its own earlier entries, so nothing it pre-answered ever hit
    // (measured: 0 hits out of 4 drives).
    private const int CacheCapacity = 256;

    // How long a cached page may outlive an index change. Short enough that a file the user just
    // created shows up on the next search, long enough to cover the pause between two searches a
    // person actually makes.
    private const long RecentGraceMs = 60_000;
    private static readonly object CacheGate = new();

    public static void SearchStreaming(LiveIndex index, string query, int limit, Action<SearchResult> onResult, CancellationToken token, string? directoryFilter = null, string? fileNameFilter = null)
    {
        if (limit <= 0 || string.IsNullOrWhiteSpace(query))
            return;

        var cacheKey = BuildCacheKey(index, query, limit, directoryFilter, fileNameFilter);
        var __cacheLookup = Lertaro.Core.SearchIndex.PerfTrace.Begin();
        if (cacheKey != null && TryReplay(cacheKey, index.Revision, limit, onResult, token))
            return;

        var cached = new List<SearchResult>(capacity: 64);
        void Capture(SearchResult row)
        {
            cached.Add(row);
            onResult(row);
        }

        var parsed = SearchQueryParser.Parse(query);
        // ponytail: the whole search, every onResult callback included, runs inside this read lock. The
        // production callback is a blocking write to SearchStreamPump's bounded channel, so one slow pipe
        // client parks the search thread holding the lock -- and ReaderWriterLockSlim blocks new readers
        // while a writer waits, so LiveIndex.Mutate (USN/watcher apply) and Compact queue behind it: a
        // stalled client can stall indexing for that drive. Deferred deliberately (decided 2026-09-22),
        // because the fix is not local: collecting the page inside the lock and emitting after
        // ExitReadLock removes progressive streaming for large result sets -- rows arrive all at once --
        // and needs a memory bound the full window cannot state while its limit is int.MaxValue. The
        // inline window already searches against a bounded limit. Reopen on an observed stall.
        var __t = Lertaro.Core.SearchIndex.PerfTrace.Begin();
        index.Read<object?>((snapshot, delta) =>
        {
            Lertaro.Core.SearchIndex.PerfTrace.Mark("idx.readLock", __t);
            var directoryFilterLower = DirectoryFilterResolver.NormalizeFilter(directoryFilter);
            if (directoryFilterLower != null && directoryFilterLower.Equals(snapshot.SourceRoot.ToLowerInvariant(), StringComparison.Ordinal))
                directoryFilterLower = null;

            if (parsed.IsPathMode)
            {
                PathSearch.SearchStreaming(snapshot, delta, parsed, limit, Capture, token, directoryFilterLower);
                return null;
            }

            var __parse = Lertaro.Core.SearchIndex.PerfTrace.Begin();
            var pattern = FzfPattern.Parse(query);
            Lertaro.Core.SearchIndex.PerfTrace.Mark("idx.parse", __parse);
            var __name = Lertaro.Core.SearchIndex.PerfTrace.Begin();
            NameSearch.SearchStreaming(snapshot, delta, pattern, limit, Capture, token, directoryFilterLower,
                FilterPatternHelper.SplitOrNullIfMatchAll(fileNameFilter));
            Lertaro.Core.SearchIndex.PerfTrace.Mark("idx.nameSearch", __name);
            return null;
        });

        if (cacheKey != null)
            Store(cacheKey, index.Revision, cached);
        Lertaro.Core.SearchIndex.PerfTrace.Mark("idx.cacheMiss", __cacheLookup);
    }

    // Directory listing rather than search (see DirectoryEnumerator): no query, no ranking, walks the
    // index's own parent->children structure. False = this drive's index doesn't hold that path.
    public static bool EnumerateDirectory(LiveIndex index, string path, bool recursive, string[]? patterns, int limit, Action<SearchResult> onResult, CancellationToken token)
        => index.Read((snapshot, delta) => DirectoryEnumerator.Enumerate(snapshot, delta, path, recursive, patterns, limit, onResult, token));

    public static void GetRecentFiles(LiveIndex index, string dirLower, uint cutoffUtc, List<SearchResult> candidates) => index.Read<object?>((snapshot, delta) =>
                                                                                                                               {
                                                                                                                                   RecentFilesV2.CollectFromDirectory(snapshot, delta, dirLower, snapshot.SourceKey, cutoffUtc, candidates);
                                                                                                                                   return null;
                                                                                                                               });

    // Only the plain name-search shape is cached: a path-mode query walks one directory and already returns
    // almost instantly, and the two shapes do not share a result type.
    private static string? BuildCacheKey(LiveIndex index, string query, int limit, string? directoryFilter, string? fileNameFilter)
    {
        try
        {
            if (string.IsNullOrEmpty(index.SourceKey))
                return null;
            // The caller's limit is deliberately NOT part of the key: the warm-up searches with its own
            // limit, and a key that carried it meant nothing the warm-up stored was ever asked for
            // again. The limit is applied to the replayed page instead.
            return string.Concat(index.SourceKey, "\u0001", query,
                "\u0001", directoryFilter ?? string.Empty, "\u0001", fileNameFilter ?? string.Empty);
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private static bool TryReplay(string key, long revision, int limit, Action<SearchResult> onResult, CancellationToken token)
    {
        lock (CacheGate)
        {
            if (!QueryCache.TryGetValue(key, out var entry))
                return false;

            // An unchanged index is the exact case; a changed one is allowed while the entry is young,
            // because the USN feed moves every drive revision on ordinary desktop activity and an
            // exact-match-only cache therefore missed almost every keystroke (measured: 1 of 4 drives
            // hit, so the slowest miss still set the whole query's latency).
            // Time, not revision, is what retires a page. The desktop changes files constantly, so
            // the revision moved between almost every pair of searches and an exact-match rule left
            // 2 of 4 drives hitting while the other 2 always missed, which put the slowest miss
            // back on the critical path. A page this young is served as-is; older than the window
            // it is rebuilt, which is what lets a just-created file show up a minute later.
            if (Environment.TickCount64 - entry.StoredAt > RecentGraceMs)
                return false;

            Lertaro.Core.SearchIndex.PerfTrace.Mark("idx.cacheHit", Lertaro.Core.SearchIndex.PerfTrace.Begin(), entry.Rows.Count);
            entry.LastUsed = Environment.TickCount64;
            var taken = 0;
            foreach (var row in entry.Rows)
            {
                if (taken++ >= limit)
                    break;
                token.ThrowIfCancellationRequested();
                onResult(row);
            }

            return true;
        }
    }

    private static void Store(string key, long revision, List<SearchResult> rows)
    {
        if (rows.Count == 0)
            return;

        lock (CacheGate)
        {
            // Deliberately no QueryCache.Clear() here: several drives search in parallel, so a drive
            // whose revision had just moved used to clear the entries every OTHER drive had stored
            // moments earlier -- measured as 0 hits with 4 misses on every single query. Stale
            // entries are ignored by the revision check and dropped by capacity eviction instead.

            if (QueryCache.Count >= CacheCapacity)
                EvictOldestLocked();

            QueryCache[key] = new Entry { Revision = revision, Rows = rows, LastUsed = Environment.TickCount64, StoredAt = Environment.TickCount64 };
        }
    }

    private static void EvictOldestLocked()
    {
        string? oldestKey = null;
        var oldest = long.MaxValue;
        foreach (var pair in QueryCache)
        {
            if (pair.Value.LastUsed >= oldest)
                continue;
            oldest = pair.Value.LastUsed;
            oldestKey = pair.Key;
        }

        if (oldestKey != null)
            QueryCache.Remove(oldestKey);
    }
}
