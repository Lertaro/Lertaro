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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal readonly record struct StoredEntry(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("type")] HistoryEntryKind Kind,
        [property: JsonPropertyName("time")] long Time,
        [property: JsonPropertyName("count")] int Count = 1);

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
            // it was JUST opened with. Its previous open count is carried forward so a repeatedly
            // opened item accumulates rather than resetting to one on every open.
            var previousCount = RemovePathFromAllBuckets(buckets, normalizedPath);

            if (!buckets.TryGetValue(keyword, out var list))
                buckets[keyword] = list = new List<StoredEntry>();

            list.Insert(0, new StoredEntry(normalizedPath, kind, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), previousCount + 1));
            if (list.Count > SearchHistoryBucketSupport.MaxEntriesPerKeyword)
                list.RemoveRange(SearchHistoryBucketSupport.MaxEntriesPerKeyword, list.Count - SearchHistoryBucketSupport.MaxEntriesPerKeyword);

            PersistNoLock();
            _priorityCache = BuildPriorityCache(buckets);
        }

        Changed?.Invoke();
    }

    // Removes any existing record of `path` from every keyword bucket, pruning a bucket entirely once
    // it's left empty so a keyword that no longer points to anything doesn't linger in the JSON file.
    // Returns the open count the removed record carried (0 when the path was not recorded before), so
    // Record can continue the count instead of restarting it.
    private static int RemovePathFromAllBuckets(Dictionary<string, List<StoredEntry>> buckets, string path)
    {
        var previousCount = 0;
        List<string>? emptied = null;
        foreach (var (keyword, list) in buckets)
        {
            var removed = list.Find(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (removed != default)
                previousCount = removed.Count;
            list.RemoveAll(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (list.Count == 0)
                (emptied ??= new List<string>()).Add(keyword);
        }
        if (emptied != null)
            foreach (var keyword in emptied)
                buckets.Remove(keyword);
        return previousCount;
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
            _buckets = BuildBuckets(validated);
            PersistNoLock();
            _priorityCache = BuildPriorityCache(_buckets);
        }

        Changed?.Invoke();
    }

    public static IReadOnlyDictionary<string, int> Snapshot()
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            return _priorityCache != null
                ? new Dictionary<string, int>(_priorityCache, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
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
    }

    private static Dictionary<string, List<StoredEntry>> LoadNoLock() => LoadFromFiles(HistoryPath, BackupPath);

    internal static Dictionary<string, List<StoredEntry>> LoadFromFiles(string mainPath, string backupPath)
    {
        // A missing main file is a fresh store: backups must not resurrect history after the file was
        // deliberately deleted. An existing file that cannot be read or parsed falls back to the
        // backup the atomic writer left behind, because an empty store would let the next save wipe
        // the user's history permanently.
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

    // Rebuilds a clean bucket set from a most-recent-first sequence: a path lands in whichever keyword
    // bucket its first (i.e. most recent) occurrence names, and each bucket stops accepting entries
    // once it reaches the per-keyword cap. Never creates a bucket entry until it actually accepts one --
    // a keyword whose only candidate loses to an earlier duplicate under a different keyword must not
    // linger as an empty bucket.
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

    // Global rank per distinct path (lowest = most recently relevant). Paths no longer span multiple
    // buckets, but this stays dedup-safe regardless.
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
