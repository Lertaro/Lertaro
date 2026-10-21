using System.Text.Json;
using System.Text.Json.Serialization;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Core;

// Stored as JSON, grouped by the keyword that was typed when the entry was opened (the top-level
// object's keys), each mapping to a most-recent-first list of (path, kind, time). Opening something
// with no query typed (e.g. clicking a Startup Panel tab item directly) isn't recorded at all -- see
// Record. A path lives under at most one keyword at a time: reopening it under a keyword it's already
// recorded under just moves it to the front with a fresh time, and reopening it under a DIFFERENT
// keyword moves it there instead, dropping it from its old one -- whichever keyword most recently led
// to it wins, so a path is never duplicated across groups.
public static class SearchHistoryStore
{
    private static readonly object Gate = new();
    private static Dictionary<string, List<StoredEntry>>? _buckets;
    private static Dictionary<string, int>? _priorityCache;
    private static Dictionary<string, int>? _countCache;

    /// <summary>What <see cref="Snapshot"/> returns before the store has any entries at all.</summary>
    private static readonly IReadOnlyDictionary<string, int> EmptyPriorities =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal readonly record struct StoredEntry(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("type")] HistoryEntryKind Kind,
        [property: JsonPropertyName("time")] long Time,
        [property: JsonPropertyName("count")] int Count = 1,
        [property: JsonPropertyName("score")] double Score = 0);

    public static string HistoryPath => Path.Combine(Logger.UserDataDir, "search-history.json");
    private static string BackupPath => HistoryPath + ".bak";

    /// <summary>Raised after the store changes, so the settings list can refresh its counts live.</summary>
    public static event Action? Changed;

    /// <param name="keyword">The search box text at the time this was opened. Nothing is recorded if
    /// this is empty -- opening something directly from a Startup Panel tab (no query typed) isn't
    /// "search history".</param>
    public static void Record(string keyword, string path, HistoryEntryKind kind)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("__", StringComparison.Ordinal))
            return;
        if (string.IsNullOrWhiteSpace(keyword))
            return;

        // File.Exists/Directory.Exists below have no timeout and can block for seconds on a slow or
        // heavily-indexed network share -- callers invoke this synchronously right before/after launching
        // a result, often still on the UI thread, so recording history must not be able to freeze that.
        var enabled = UserSettings.Load().EnableHistory;
        Task.Run(() => RecordCore(keyword.Trim(), path, kind, enabled));
    }

    private static void RecordCore(string keyword, string path, HistoryEntryKind kind, bool enabled)
    {
        var isApp = kind == HistoryEntryKind.Application;
        var normalizedPath = isApp ? path.Trim() : NormalizePath(path);
        if (!ExistsForKind(normalizedPath, kind, File.Exists, Directory.Exists))
            return;

        lock (Gate)
        {
            EnsureCacheNoLock();
            var buckets = _buckets!;

            var entryExists = buckets.Values.Any(list => list.Any(e =>
                e.Path.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)));
            if (!HistoryRecordingPolicy.ShouldRecord(enabled, entryExists))
                return;

            // A path belongs to at most one keyword -- drop it from wherever it currently lives
            // (including its own bucket, if it's already there) before re-adding it under the keyword
            // it was JUST opened with. Its previous open count and decayed score are carried forward so a
            // repeatedly opened item accumulates rather than restarting on every open.
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var previous = RemovePathFromAllBuckets(buckets, normalizedPath);
            var previousScore = previous is { } p
                ? SearchHistoryBucketSupport.EffectiveScore(p, now)
                : 0d;
            var score = Math.Min(SearchHistoryBucketSupport.ScoreCap, previousScore + 1d);

            if (!buckets.TryGetValue(keyword, out var list))
                buckets[keyword] = list = new List<StoredEntry>();

            list.Insert(0, new StoredEntry(normalizedPath, kind, now, (previous?.Count ?? 0) + 1, score));
            if (list.Count > SearchHistoryBucketSupport.MaxEntriesPerKeyword)
                list.RemoveRange(SearchHistoryBucketSupport.MaxEntriesPerKeyword, list.Count - SearchHistoryBucketSupport.MaxEntriesPerKeyword);

            PersistNoLock();
            _priorityCache = BuildPriorityCache(buckets);
            _countCache = BuildCountCache(buckets);
        }

        Changed?.Invoke();
    }

    // Removes any existing record of `path` from every keyword bucket, pruning a bucket entirely once
    // it's left empty so a keyword that no longer points to anything doesn't linger in the JSON file.
    // Returns the record that was dropped (null when the path was not recorded before) so Record can
    // continue its count and decay its score instead of restarting both.
    private static StoredEntry? RemovePathFromAllBuckets(Dictionary<string, List<StoredEntry>> buckets, string path)
    {
        StoredEntry? previous = null;
        List<string>? emptied = null;
        foreach (var (keyword, list) in buckets)
        {
            var removed = list.Find(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (removed != default)
                previous = removed;
            list.RemoveAll(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (list.Count == 0)
                (emptied ??= new List<string>()).Add(keyword);
        }
        if (emptied != null)
            foreach (var keyword in emptied)
                buckets.Remove(keyword);
        return previous;
    }

    /// <summary>Ranking boost lookup -- keyed by the bare target path, regardless of which keyword it's recorded under.</summary>
    public static int GetPriority(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return int.MaxValue;

        var normalized = NormalizePath(path);
        lock (Gate)
        {
            EnsureCacheNoLock();
            return _priorityCache != null && _priorityCache.TryGetValue(normalized, out var priority)
                ? priority
                : int.MaxValue;
        }
    }

    /// <summary>Every entry across every keyword, most-recently-opened first.</summary>
    public static IReadOnlyList<HistoryEntry> GetEntries()
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            return Flatten(_buckets!);
        }
    }

    /// <summary>Replaces the whole store with exactly these entries (Settings' edited/removed list).</summary>
    public static void SaveEntries(IEnumerable<HistoryEntry> entries)
    {
        // Network-path probes must happen outside Gate (see Record's comment above): File.Exists/
        // Directory.Exists have no timeout and can block for seconds on a slow share, and Gate is
        // taken by Snapshot/GetPriority on every keystroke's search. Only the commit holds the lock.
        var validated = new List<(string Keyword, StoredEntry Entry)>();
        foreach (var entry in entries.OrderByDescending(e => e.Time))
        {
            var isApp = entry.Kind == HistoryEntryKind.Application;
            var normalizedPath = isApp ? entry.Path.Trim() : NormalizePath(entry.Path);
            if (!ExistsForKind(normalizedPath, entry.Kind, File.Exists, Directory.Exists))
                continue;

            validated.Add((entry.Keyword?.Trim() ?? string.Empty, new StoredEntry(normalizedPath, entry.Kind, entry.Time, entry.Count)));
        }

        lock (Gate)
        {
            // A HistoryEntry carries no score, so an edit or a removal on the settings page must not
            // silently reset every untouched path's weight to its bare open count. Carry the stored score
            // over by path before rebuilding; anything the store has never seen falls back to the
            // count-based migration.
            EnsureCacheNoLock();
            var storedScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var list in _buckets!.Values)
                foreach (var entry in list)
                    if (entry.Score > 0d)
                        storedScores[entry.Path] = entry.Score;

            var scored = new List<(string Keyword, StoredEntry Entry)>(validated.Count);
            foreach (var (keyword, entry) in validated)
                scored.Add((keyword, entry with
                {
                    Score = storedScores.TryGetValue(entry.Path, out var stored)
                        ? stored
                        : Math.Min(SearchHistoryBucketSupport.ScoreCap, entry.Count),
                }));

            _buckets = BuildBuckets(scored);
            PersistNoLock();
            _priorityCache = BuildPriorityCache(_buckets);
            _countCache = BuildCountCache(_buckets);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// The live path-to-rank map. Handed out by reference: <see cref="_priorityCache"/> is only ever
    /// replaced with a freshly built dictionary (never mutated in place), so a reader holding the
    /// reference sees a consistent snapshot, and the callers are per-keystroke -- copying it used to
    /// allocate and discard the whole dictionary while holding the gate the recorder needs.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Snapshot()
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            return _priorityCache ?? EmptyPriorities;
        }
    }

    /// <summary>
    /// Path -> how many times it was opened (the stored count, not the bucket position). The ranking
    /// windows use this to give a path with five or more opens a stronger history band; paths with one to
    /// four opens only break ties after every match key.
    /// </summary>
    public static IReadOnlyDictionary<string, int> UsageCounts()
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            return _countCache ?? EmptyPriorities;
        }
    }

    /// <summary>Ranking lookup: path -> effective history score * 100 (0.01 precision), decayed at read time.
    /// The decay is never persisted here; only a new open rewrites Time/Score. Callers take one snapshot per query.</summary>
    public static IReadOnlyDictionary<string, int> ScoreSnapshot(DateTimeOffset now)
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            return SearchHistoryBucketSupport.BuildScoreCache(_buckets!, now.ToUnixTimeSeconds());
        }
    }

    private static Dictionary<string, int> BuildCountCache(Dictionary<string, List<StoredEntry>> buckets)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in buckets.Values)
            foreach (var entry in list)
                counts[entry.Path] = entry.Count;
        return counts;
    }

    internal static bool ExistsForKind(
        string path,
        HistoryEntryKind kind,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists) => kind switch
        {
            HistoryEntryKind.Application => true,
            HistoryEntryKind.Folder => directoryExists(path),
            _ => fileExists(path)
        };

    private static void EnsureCacheNoLock()
    {
        if (_buckets != null)
            return;

        _buckets = LoadNoLock();
        _priorityCache = BuildPriorityCache(_buckets);
        _countCache = BuildCountCache(_buckets);
    }

    private static Dictionary<string, List<StoredEntry>> LoadNoLock() => LoadFromFiles(HistoryPath, BackupPath);

    internal static Dictionary<string, List<StoredEntry>> LoadFromFiles(string mainPath, string backupPath)
    {
        // Missing main file = fresh store; a corrupt existing file falls back to backup -- see KeywordHistoryStore.LoadFromFiles.
        if (!File.Exists(mainPath))
            return new Dictionary<string, List<StoredEntry>>(StringComparer.OrdinalIgnoreCase);

        return TryLoadFile(mainPath)
            ?? TryLoadFile(backupPath)
            ?? new Dictionary<string, List<StoredEntry>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Parses one history file into re-derived buckets; null when it cannot be read or parsed.</summary>
    internal static Dictionary<string, List<StoredEntry>>? TryLoadFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, List<StoredEntry>>>(File.ReadAllText(path), JsonOptions);
            if (raw == null)
                return null;

            // Re-derive through the same one-path-one-keyword + per-keyword-cap rule RecordCore/
            // SaveEntries enforce -- retroactively fixes up a file that predates this rule (or was
            // hand-edited) instead of just trusting whatever's on disk.
            var flat = raw.SelectMany(kv => kv.Value.Select(e => (Keyword: kv.Key, Entry: e)))
                .OrderByDescending(x => x.Entry.Time);
            return BuildBuckets(flat);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchHistoryStore] Failed to read history from '{path}': {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    // Thin forwarder -- see SearchHistoryBucketSupport.BuildBuckets for the bucket-rebuild rules.
    private static Dictionary<string, List<StoredEntry>> BuildBuckets(IEnumerable<(string Keyword, StoredEntry Entry)> mostRecentFirst)
        => SearchHistoryBucketSupport.BuildBuckets(mostRecentFirst);

    private static void PersistNoLock()
    {
        try
        {
            Directory.CreateDirectory(Logger.UserDataDir);
            AtomicFileStore.Write(HistoryPath, JsonSerializer.Serialize(_buckets, JsonOptions), BackupPath);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchHistoryStore] Failed to write history: {ex.Message}", LogLevel.Error);
        }
    }

    private static List<HistoryEntry> Flatten(Dictionary<string, List<StoredEntry>> buckets)
        => SearchHistoryBucketSupport.Flatten(buckets);

    // Thin forwarder -- see SearchHistoryBucketSupport.BuildPriorityCache for the ranking rules.
    private static Dictionary<string, int> BuildPriorityCache(Dictionary<string, List<StoredEntry>> buckets)
        => SearchHistoryBucketSupport.BuildPriorityCache(buckets);

    internal static string NormalizePath(string path)
    {
        var normalized = path.Trim().Trim('"')
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        try
        {
            if (!WslPath.IsPath(normalized))
                normalized = Path.GetFullPath(normalized);
        }
        catch
        {
        }

        return normalized.TrimEnd(Path.DirectorySeparatorChar);
    }
}
