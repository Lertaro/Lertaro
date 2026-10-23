using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Core;

// Separates two distinct signals that V1/V2 conflated in one bucket table:
//   1. Path frecency (search-history.json): decay-weighted open counts per path, used only to break ties
//      among direct matches inside the same EvidenceGrade.
//   2. Canonical query -> path associations (search-query-associations.json): organic opens linking a
//      canonicalized query string to a concrete target path. Cross-name recall ("LearnedAlias") requires at
//      least MinAssociationOrganicOpens organic opens, never self-reinforces when opened via LearnedAlias
//      recall alone, and migrates legacy history counts clamped to <= 1 so historical mis-picks fade cleanly.
public static class SearchHistoryStore
{
    public const int MinAssociationOrganicOpens = 3;

    private static readonly object Gate = new();
    private static Dictionary<string, List<StoredEntry>>? _buckets;
    private static Dictionary<string, List<StoredAssociationEntry>>? _associations;
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

    internal readonly record struct StoredAssociationEntry(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("type")] HistoryEntryKind Kind,
        [property: JsonPropertyName("time")] long LastOrganicTime,
        [property: JsonPropertyName("organicOpens")] int OrganicOpens = 1);

    public static string HistoryPath => Path.Combine(Logger.UserDataDir, "search-history.json");
    private static string BackupPath => HistoryPath + ".bak";

    public static string AssociationsPath => Path.Combine(Logger.UserDataDir, "search-query-associations.json");
    private static string AssociationsBackupPath => AssociationsPath + ".bak";

    /// <summary>Raised after the store changes, so the settings list can refresh its counts live.</summary>
    public static event Action? Changed;

    /// <param name="keyword">The search box text at the time this was opened. Nothing is recorded if
    /// this is empty -- opening something directly from a Startup Panel tab (no query typed) isn't
    /// "search history".</param>
    /// <param name="fromLearnedRecall">True when the user launched a row that was recalled only via
    /// <c>LearnedAlias</c>; refreshes path recency without incrementing organic query-association evidence.</param>
    public static void Record(string keyword, string path, HistoryEntryKind kind, bool fromLearnedRecall = false)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("__", StringComparison.Ordinal))
            return;
        if (string.IsNullOrWhiteSpace(keyword))
            return;

        var enabled = UserSettings.Load().EnableHistory;
        Task.Run(() => RecordCore(keyword.Trim(), path, kind, enabled, fromLearnedRecall));
    }

    private static void RecordCore(
        string keyword,
        string path,
        HistoryEntryKind kind,
        bool enabled,
        bool fromLearnedRecall)
    {
        var isApp = kind == HistoryEntryKind.Application;
        var normalizedPath = isApp ? path.Trim() : NormalizePath(path);
        if (!ExistsForKind(normalizedPath, kind, File.Exists, Directory.Exists))
            return;

        lock (Gate)
        {
            EnsureCacheNoLock();
            var buckets = _buckets!;
            var associations = _associations!;

            var entryExists = buckets.Values.Any(list => list.Any(e =>
                e.Path.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)));
            if (!HistoryRecordingPolicy.ShouldRecord(enabled, entryExists))
                return;

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

            RecordAssociationInMemory(associations, keyword, normalizedPath, kind, now, fromLearnedRecall);

            PersistNoLock();
            PersistAssociationsNoLock();
            _priorityCache = BuildPriorityCache(buckets);
            _countCache = BuildCountCache(buckets);
        }

        Changed?.Invoke();
    }

    internal static void RecordAssociationInMemory(
        Dictionary<string, List<StoredAssociationEntry>> associations,
        string rawQuery,
        string normalizedPath,
        HistoryEntryKind kind,
        long nowUnix,
        bool fromLearnedRecall)
    {
        // Learned-alias clicks must not self-reinforce an unconfirmed association or create new ones.
        if (fromLearnedRecall)
            return;

        var canonicalQuery = NormalizeAssociationQuery(rawQuery);
        if (canonicalQuery.Length == 0)
            return;

        if (!associations.TryGetValue(canonicalQuery, out var list))
            associations[canonicalQuery] = list = new List<StoredAssociationEntry>();

        var existingIndex = list.FindIndex(entry =>
            entry.Path.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
        var previousOpens = existingIndex >= 0 ? list[existingIndex].OrganicOpens : 0;
        if (existingIndex >= 0)
            list.RemoveAt(existingIndex);

        list.Insert(0, new StoredAssociationEntry(
            normalizedPath,
            kind,
            nowUnix,
            Math.Max(1, previousOpens + 1)));

        if (list.Count > SearchHistoryBucketSupport.MaxEntriesPerKeyword)
            list.RemoveRange(SearchHistoryBucketSupport.MaxEntriesPerKeyword, list.Count - SearchHistoryBucketSupport.MaxEntriesPerKeyword);
    }

    /// <summary>
    /// Canonical query normalization for learned associations: trims, collapses internal whitespace, and
    /// case-folds. Then reuses P4's joined-reading qualification: two or more all-letter ASCII terms whose
    /// concatenation is still a valid term collapse to that joined form, so "sousuo jieguo" and
    /// "sousuojieguo" are one association key. Anything else keeps its spacing.
    /// </summary>
    public static string NormalizeAssociationQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;

        var span = query.AsSpan().Trim();
        var sb = new StringBuilder(span.Length);
        var prevSpace = false;
        foreach (var ch in span)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
            }
            else
            {
                sb.Append(char.ToLowerInvariant(ch));
                prevSpace = false;
            }
        }

        return TryJoinAsciiTerms(sb) ?? sb.ToString();
    }

    // P4's joined branch accepts a query of two or more plain all-letter ASCII terms (2..63 characters
    // each) and scores the concatenation as an alternative reading. The store must use the same
    // qualification, or a learned click on one spelling would never be found from the other.
    private static string? TryJoinAsciiTerms(StringBuilder normalized)
    {
        var joined = new StringBuilder(normalized.Length);
        var tokenLength = 0;
        var tokenCount = 0;
        for (var i = 0; i < normalized.Length; i++)
        {
            var ch = normalized[i];
            if (ch == ' ')
            {
                if (tokenLength is < 2 or > 63)
                    return null;
                tokenCount++;
                tokenLength = 0;
                continue;
            }

            if (ch is < 'a' or > 'z')
                return null;
            joined.Append(ch);
            tokenLength++;
        }

        if (tokenLength is < 2 or > 63)
            return null;
        tokenCount++;
        return tokenCount >= 2 && joined.Length is >= 2 and <= 63 ? joined.ToString() : null;
    }

    internal static Dictionary<string, List<StoredAssociationEntry>> MigrateAssociationsFromLegacyBuckets(
        Dictionary<string, List<StoredEntry>> buckets)
    {
        var result = new Dictionary<string, List<StoredAssociationEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (keyword, entries) in buckets)
        {
            var canonical = NormalizeAssociationQuery(keyword);
            if (canonical.Length == 0)
                continue;

            if (!result.TryGetValue(canonical, out var list))
                result[canonical] = list = new List<StoredAssociationEntry>();

            foreach (var entry in entries)
            {
                // Legacy bucket counts mixed frecency with self-reinforced learned opens: clamp initial
                // organic count to <= 1 so historical mis-picks drop out until re-confirmed organically.
                MergeSingleAssociationEntry(
                    list,
                    new StoredAssociationEntry(
                        entry.Path,
                        entry.Kind,
                        entry.Time,
                        OrganicOpens: Math.Clamp(entry.Count, 0, 1)),
                    sumOpens: false);
            }

            // Several legacy keywords fold into one canonical query, and the reader keeps the newest
            // MaxEntriesPerKeyword rows per key. Do the same here, or the surplus is written once and then
            // silently dropped on the next load -- which rows survive must not depend on how the buckets
            // happened to be stored.
            TrimAssociationList(list);
        }
        return result;
    }

    private static void MergeSingleAssociationEntry(
        List<StoredAssociationEntry> list,
        StoredAssociationEntry entry,
        bool sumOpens)
    {
        var opens = Math.Max(0, entry.OrganicOpens);
        var index = list.FindIndex(existing =>
            existing.Path.Equals(entry.Path, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            list.Add(entry with { OrganicOpens = opens });
            return;
        }

        var current = list[index];
        var mergedOpens = sumOpens
            ? (int)Math.Min(int.MaxValue, (long)current.OrganicOpens + opens)
            : Math.Max(current.OrganicOpens, opens);
        var newest = entry.LastOrganicTime >= current.LastOrganicTime ? entry : current;
        list[index] = new StoredAssociationEntry(
            newest.Path,
            newest.Kind,
            Math.Max(current.LastOrganicTime, entry.LastOrganicTime),
            mergedOpens);
    }

    private static void TrimAssociationList(List<StoredAssociationEntry> list)
    {
        list.Sort(static (x, y) => y.LastOrganicTime.CompareTo(x.LastOrganicTime));
        if (list.Count > SearchHistoryBucketSupport.MaxEntriesPerKeyword)
        {
            list.RemoveRange(
                SearchHistoryBucketSupport.MaxEntriesPerKeyword,
                list.Count - SearchHistoryBucketSupport.MaxEntriesPerKeyword);
        }
    }

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

    /// <summary>All stored query -> path associations (including below-threshold ones) for inspection/editing in Settings.</summary>
    public static IReadOnlyList<HistoryEntry> GetAssociationEntries()
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            return FlattenAssociations(_associations!, minOrganicOpens: 1);
        }
    }

    /// <summary>Confirmed query -> path associations eligible for <c>LearnedAlias</c> recall (<c>OrganicOpens &gt;= 3</c>).</summary>
    public static IReadOnlyList<HistoryEntry> GetLearnedAssociationEntries()
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            return FlattenAssociations(_associations!, MinAssociationOrganicOpens);
        }
    }

    internal static List<HistoryEntry> FlattenAssociations(
        Dictionary<string, List<StoredAssociationEntry>> associations,
        int minOrganicOpens)
    {
        return associations
            .SelectMany(kv => kv.Value
                .Where(entry => entry.OrganicOpens >= minOrganicOpens)
                .Select(entry => new HistoryEntry(kv.Key, entry.Path, entry.Kind, entry.LastOrganicTime, entry.OrganicOpens)))
            .OrderByDescending(entry => entry.Time)
            .ThenByDescending(entry => entry.Count)
            .ToList();
    }

    /// <summary>Replaces the whole path-frecency store with these entries and prunes removed paths from associations.</summary>
    public static void SaveEntries(IEnumerable<HistoryEntry> entries)
    {
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
            var survivingPaths = new HashSet<string>(
                scored.Select(item => item.Entry.Path),
                StringComparer.OrdinalIgnoreCase);
            PruneAssociationsToSurvivingPaths(_associations!, survivingPaths);

            PersistNoLock();
            PersistAssociationsNoLock();
            _priorityCache = BuildPriorityCache(_buckets);
            _countCache = BuildCountCache(_buckets);
        }

        Changed?.Invoke();
    }

    /// <summary>Replaces stored query -> path associations independently from path frecency.</summary>
    public static void SaveAssociationEntries(IEnumerable<HistoryEntry> entries)
    {
        var rebuilt = new Dictionary<string, List<StoredAssociationEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.OrderByDescending(e => e.Time))
        {
            var canonical = NormalizeAssociationQuery(entry.Keyword);
            if (canonical.Length == 0)
                continue;

            var isApp = entry.Kind == HistoryEntryKind.Application;
            var normalizedPath = isApp ? entry.Path.Trim() : NormalizePath(entry.Path);
            if (!ExistsForKind(normalizedPath, entry.Kind, File.Exists, Directory.Exists))
                continue;

            if (!rebuilt.TryGetValue(canonical, out var list))
                rebuilt[canonical] = list = new List<StoredAssociationEntry>();
            MergeSingleAssociationEntry(
                list,
                new StoredAssociationEntry(normalizedPath, entry.Kind, entry.Time, Math.Max(1, entry.Count)),
                sumOpens: false);
        }

        foreach (var list in rebuilt.Values)
            TrimAssociationList(list);

        lock (Gate)
        {
            EnsureCacheNoLock();
            _associations = rebuilt;
            PersistAssociationsNoLock();
        }

        Changed?.Invoke();
    }

    /// <summary>Clears all learned query -> path associations without resetting path frecency.</summary>
    public static void ClearQueryAssociations()
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            _associations = new Dictionary<string, List<StoredAssociationEntry>>(StringComparer.OrdinalIgnoreCase);
            PersistAssociationsNoLock();
        }

        Changed?.Invoke();
    }

    private static void PruneAssociationsToSurvivingPaths(
        Dictionary<string, List<StoredAssociationEntry>> associations,
        HashSet<string> survivingPaths)
    {
        List<string>? emptied = null;
        foreach (var (query, list) in associations)
        {
            list.RemoveAll(entry => !survivingPaths.Contains(entry.Path));
            if (list.Count == 0)
                (emptied ??= new List<string>()).Add(query);
        }
        if (emptied != null)
        {
            foreach (var query in emptied)
                associations.Remove(query);
        }
    }

    public static IReadOnlyDictionary<string, int> Snapshot()
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            return _priorityCache ?? EmptyPriorities;
        }
    }

    public static IReadOnlyDictionary<string, int> UsageCounts()
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            return _countCache ?? EmptyPriorities;
        }
    }

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
        if (_buckets == null)
        {
            _buckets = LoadNoLock();
            _priorityCache = BuildPriorityCache(_buckets);
            _countCache = BuildCountCache(_buckets);
        }

        if (_associations == null)
        {
            _associations = LoadAssociationsFromFiles(AssociationsPath, AssociationsBackupPath, _buckets);
        }
    }

    private static Dictionary<string, List<StoredEntry>> LoadNoLock() => LoadFromFiles(HistoryPath, BackupPath);

    internal static Dictionary<string, List<StoredAssociationEntry>> LoadAssociationsFromFiles(
        string mainPath,
        string backupPath,
        Dictionary<string, List<StoredEntry>> legacyBuckets)
    {
        if (!File.Exists(mainPath))
            return MigrateAssociationsFromLegacyBuckets(legacyBuckets);

        return TryLoadAssociationsFile(mainPath)
            ?? TryLoadAssociationsFile(backupPath)
            // Both unreadable is not "no associations": the buckets still hold every recorded open, and
            // re-migrating them keeps the learned pairs recoverable instead of ending the feature with a
            // truncated file. The clamped organic counts mean nothing is recalled until it is re-confirmed.
            ?? MigrateAssociationsFromLegacyBuckets(legacyBuckets);
    }

    internal static Dictionary<string, List<StoredAssociationEntry>>? TryLoadAssociationsFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, List<StoredAssociationEntry>>>(File.ReadAllText(path), JsonOptions);
            if (raw == null)
                return null;

            var normalized = new Dictionary<string, List<StoredAssociationEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, entries) in raw)
            {
                var canonical = NormalizeAssociationQuery(key);
                if (canonical.Length == 0 || entries.Count == 0)
                    continue;
                if (!normalized.TryGetValue(canonical, out var list))
                    normalized[canonical] = list = new List<StoredAssociationEntry>();

                // Several on-disk spellings can fold into one canonical key ("sousuo jieguo" and
                // "sousuojieguo"). Keep every path and add the organic opens: those were separate user
                // confirmations, so dropping one spelling would lose recall evidence.
                foreach (var entry in entries)
                    MergeSingleAssociationEntry(list, entry, sumOpens: true);
                TrimAssociationList(list);
            }
            return normalized;
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchHistoryStore] Failed to read query associations from '{path}': {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    internal static Dictionary<string, List<StoredEntry>> LoadFromFiles(string mainPath, string backupPath)
    {
        if (!File.Exists(mainPath))
            return new Dictionary<string, List<StoredEntry>>(StringComparer.OrdinalIgnoreCase);

        return TryLoadFile(mainPath)
            ?? TryLoadFile(backupPath)
            ?? new Dictionary<string, List<StoredEntry>>(StringComparer.OrdinalIgnoreCase);
    }

    internal static Dictionary<string, List<StoredEntry>>? TryLoadFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, List<StoredEntry>>>(File.ReadAllText(path), JsonOptions);
            if (raw == null)
                return null;

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

    private static void PersistAssociationsNoLock()
    {
        try
        {
            Directory.CreateDirectory(Logger.UserDataDir);
            AtomicFileStore.Write(AssociationsPath, JsonSerializer.Serialize(_associations, JsonOptions), AssociationsBackupPath);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchHistoryStore] Failed to write query associations: {ex.Message}", LogLevel.Error);
        }
    }

    private static List<HistoryEntry> Flatten(Dictionary<string, List<StoredEntry>> buckets)
        => SearchHistoryBucketSupport.Flatten(buckets);

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
