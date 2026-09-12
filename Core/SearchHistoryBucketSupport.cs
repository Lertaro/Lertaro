using Lertaro.PluginSdk.Services;

namespace Lertaro.Core;

// Pure shaping of SearchHistoryStore's buckets -- building them from a flat sequence, flattening them
// back into display entries, and deriving the per-path priority cache. Split out purely to keep
// SearchHistoryStore under the repo's per-file line limit; this class has no state of its own, it
// always operates on the bucket dictionary handed to it.
internal static class SearchHistoryBucketSupport
{
    internal const int MaxEntriesPerKeyword = 20;

    // Rebuilds a clean bucket set from a most-recent-first sequence: a path lands in whichever keyword
    // bucket its first (i.e. most recent) occurrence names, and each bucket stops accepting entries
    // once it reaches the per-keyword cap. Never creates a bucket entry until it actually accepts one --
    // a keyword whose only candidate loses to an earlier duplicate under a different keyword must not
    // linger as an empty bucket.
    internal static Dictionary<string, List<SearchHistoryStore.StoredEntry>> BuildBuckets(
        IEnumerable<(string Keyword, SearchHistoryStore.StoredEntry Entry)> mostRecentFirst)
    {
        var buckets = new Dictionary<string, List<SearchHistoryStore.StoredEntry>>(StringComparer.OrdinalIgnoreCase);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (keyword, entry) in mostRecentFirst)
        {
            if (seenPaths.Contains(entry.Path))
                continue; // a more recent occurrence under some keyword already claimed this path

            if (buckets.TryGetValue(keyword, out var existing) && existing.Count >= MaxEntriesPerKeyword)
                continue; // this keyword is full -- an older duplicate under a different, non-full keyword may still fit

            seenPaths.Add(entry.Path);
            if (!buckets.TryGetValue(keyword, out var list))
                buckets[keyword] = list = new List<SearchHistoryStore.StoredEntry>();
            // A pre-count JSON deserializes Count to 0 (the record's default parameter does not apply
            // through System.Text.Json); normalise so an upgraded entry never reads as "never used".
            list.Add(entry with { Count = Math.Max(1, entry.Count) });
        }
        return buckets;
    }

    internal static List<HistoryEntry> Flatten(Dictionary<string, List<SearchHistoryStore.StoredEntry>> buckets)
    {
        var all = new List<HistoryEntry>();
        foreach (var (keyword, list) in buckets)
            foreach (var e in list)
                all.Add(new HistoryEntry(keyword, e.Path, e.Kind, e.Time, e.Count));

        all.Sort((a, b) => b.Time.CompareTo(a.Time));
        return all;
    }

    // Global rank per distinct path (lowest = most recently relevant). Paths no longer span multiple
    // buckets, but this stays dedup-safe regardless.
    internal static Dictionary<string, int> BuildPriorityCache(Dictionary<string, List<SearchHistoryStore.StoredEntry>> buckets)
    {
        var flat = Flatten(buckets);
        var priorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < flat.Count; i++)
        {
            if (!priorities.ContainsKey(flat[i].Path))
                priorities[flat[i].Path] = i;
        }
        return priorities;
    }
}
