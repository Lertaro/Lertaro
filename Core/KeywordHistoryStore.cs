namespace Lertaro.Core;

/// <summary>A keyword the quick window recorded, with how many times it has been typed.</summary>
public readonly record struct KeywordHistoryEntry(string Keyword, int Count = 1);

/// <summary>
/// Persists recently typed quick-window search keywords (as opposed to
/// <see cref="SearchHistoryStore"/>, which tracks opened file/folder paths). Recorded once per quick
/// window close, deduped by moving the most recent keyword to the front of the timeline while carrying
/// its open count forward.
/// </summary>
public static class KeywordHistoryStore
{
    private const int MaxEntries = 2000;
    private static readonly object Gate = new();
    private static List<KeywordHistoryEntry>? _entriesCache;

    public static string HistoryPath => Path.Combine(Logger.UserDataDir, "keyword-history.txt");

    private static string BackupPath => HistoryPath + ".bak";

    /// <summary>Raised after the store changes, so the settings list can refresh its counts live.</summary>
    public static event Action? Changed;

    public static void Record(string? keyword)
    {
        var trimmed = keyword?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return;

        var enabled = UserSettings.Load().EnableKeywordHistory;
        lock (Gate)
        {
            EnsureCacheNoLock();
            var existingIndex = _entriesCache!.FindIndex(x => x.Keyword.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
            if (!HistoryRecordingPolicy.ShouldRecord(enabled, existingIndex >= 0))
                return;

            var count = existingIndex >= 0 ? _entriesCache[existingIndex].Count + 1 : 1;
            _entriesCache.RemoveAll(x => x.Keyword.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
            _entriesCache.Insert(0, new KeywordHistoryEntry(trimmed, count));

            if (_entriesCache.Count > MaxEntries)
                _entriesCache.RemoveRange(MaxEntries, _entriesCache.Count - MaxEntries);

            SaveNoLock();
        }

        Changed?.Invoke();
    }

    public static void Delete(string keyword)
    {
        var removed = false;
        lock (Gate)
        {
            EnsureCacheNoLock();
            if (_entriesCache!.RemoveAll(x => x.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                removed = true;
                SaveNoLock();
            }
        }

        if (removed)
            Changed?.Invoke();
    }

    /// <summary>Every keyword in most-recent-first order, bare strings (open counts dropped).</summary>
    public static IReadOnlyList<string> GetEntries()
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            return _entriesCache!.Select(x => x.Keyword).ToList();
        }
    }

    /// <summary>Every keyword with its open count, most-recent-first.</summary>
    public static IReadOnlyList<KeywordHistoryEntry> GetCountedEntries()
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            return _entriesCache!.ToList();
        }
    }

    public static void SaveEntries(IEnumerable<KeywordHistoryEntry> entries)
    {
        lock (Gate)
        {
            // DistinctBy keeps the first (most recent) occurrence, and Count is pinned to at least one
            // so a hand-edited or zero count can never render an entry invisible to the "clear below
            // N" rule.
            _entriesCache = entries
                .Where(x => !string.IsNullOrWhiteSpace(x.Keyword))
                .Select(x => new KeywordHistoryEntry(x.Keyword.Trim(), Math.Max(1, x.Count)))
                .DistinctBy(x => x.Keyword, StringComparer.OrdinalIgnoreCase)
                .Take(MaxEntries)
                .ToList();

            SaveNoLock();
        }

        Changed?.Invoke();
    }

    private static void SaveNoLock()
    {
        try
        {
            Directory.CreateDirectory(Logger.UserDataDir);
            AtomicFileStore.Write(HistoryPath, ToFileContent(_entriesCache!), BackupPath);
        }
        catch (Exception ex)
        {
            Logger.Log($"[KeywordHistoryStore] Failed to write history: {ex.Message}", LogLevel.Error);
        }
    }

    // One "keyword<TAB>count" line each; empty store an empty file. The tab separator is what keeps the
    // file backward compatible with older builds that wrote bare keywords one per line.
    private static string ToFileContent(List<KeywordHistoryEntry> entries) =>
        entries.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, entries.Select(e => $"{e.Keyword}\t{e.Count}")) + Environment.NewLine;

    private static void EnsureCacheNoLock()
    {
        if (_entriesCache != null)
            return;

        _entriesCache = ReadEntriesNoLock();
    }

    private static List<KeywordHistoryEntry> ReadEntriesNoLock() => LoadFromFiles(HistoryPath, BackupPath);

    internal static List<KeywordHistoryEntry> LoadFromFiles(string mainPath, string backupPath)
    {
        // A missing main file is a fresh store: backups must not resurrect history after the file was
        // deliberately deleted. An existing file that cannot be read or parsed falls back to the
        // backup the atomic writer left behind, because an empty store would let the next save wipe
        // the user's history permanently.
        if (!File.Exists(mainPath))
            return new List<KeywordHistoryEntry>();

        return TryReadFile(mainPath) ?? TryReadFile(backupPath) ?? new List<KeywordHistoryEntry>();
    }

    /// <summary>Reads one history file into trimmed, deduped keyword/count pairs; null when it cannot
    /// be read. A line without the tab separator (pre-count format) reads as one use.</summary>
    internal static List<KeywordHistoryEntry>? TryReadFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<KeywordHistoryEntry>();
            foreach (var line in File.ReadLines(path))
            {
                var keyword = line;
                var count = 1;
                var tab = line.IndexOf('\t');
                if (tab >= 0)
                {
                    keyword = line[..tab];
                    if (!int.TryParse(line[(tab + 1)..], out count) || count < 1)
                        count = 1;
                }

                keyword = keyword.Trim();
                if (keyword.Length == 0 || !seen.Add(keyword))
                    continue;

                entries.Add(new KeywordHistoryEntry(keyword, count));
                if (entries.Count >= MaxEntries)
                    break;
            }
            return entries;
        }
        catch (Exception ex)
        {
            Logger.Log($"[KeywordHistoryStore] Failed to read history from '{path}': {ex.Message}", LogLevel.Error);
            return null;
        }
    }
}
