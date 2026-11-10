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
        public required long Revision { get; init; }
        public long LastUsed { get; set; }
    }

    private const int Capacity = 128;
    internal const int MaxRows = 1024;
    private const long IdleExpiryMs = 30_000;

    private readonly Dictionary<string, Entry> _pages = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    internal static string KeyFor(string query, string? directoryFilter, string? fileNameFilter,
        SearchCategoryFilter category) =>
        string.Concat(query, "\u0001", directoryFilter ?? string.Empty, "\u0001",
            fileNameFilter ?? string.Empty, "\u0001",
            ((byte)category).ToString(System.Globalization.CultureInfo.InvariantCulture));

    internal bool TryReplay(string key, int limit, Action<SearchResult> onResult,
        CancellationToken token, long revision)
    {
        List<SearchResult> page;
        lock (_gate)
        {
            if (!_pages.TryGetValue(key, out var entry)
                || entry.Revision != revision
                || Environment.TickCount64 - entry.LastUsed > IdleExpiryMs
                || entry.Rows.Count < limit && !(entry.Rows.Count == 0 && entry.Complete))
            {
                return false;
            }

            entry.LastUsed = Environment.TickCount64;
            page = entry.Rows.Count == limit ? entry.Rows : entry.Rows.GetRange(0, limit);
        }

        foreach (var row in page)
        {
            token.ThrowIfCancellationRequested();
            onResult(row);
        }
        return true;
    }

    internal void Store(string key, List<SearchResult> rows, int requestLimit, long revision)
    {
        if (rows.Count > MaxRows)
            rows = rows.GetRange(0, MaxRows);

        var entry = new Entry
        {
            Rows = rows,
            Complete = rows.Count < requestLimit,
            Revision = revision,
            LastUsed = Environment.TickCount64,
        };

        lock (_gate)
        {
            if (_pages.Count >= Capacity && !_pages.ContainsKey(key))
                EvictOldestLocked();
            _pages[key] = entry;
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
