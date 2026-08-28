namespace Lertaro.Core;

/// <summary>
/// Persists recently typed quick-window search keywords (as opposed to
/// <see cref="SearchHistoryStore"/>, which tracks opened file/folder paths). Recorded once per quick
/// window close, deduped by moving the most recent keyword to the front of the timeline.
/// </summary>
public static class KeywordHistoryStore
{
    private const int MaxEntries = 2000;
    private static readonly object Gate = new();
    private static List<string>? _entriesCache;

    public static string HistoryPath => Path.Combine(Logger.UserDataDir, "keyword-history.txt");

    private static string BackupPath => HistoryPath + ".bak";

    public static void Record(string? keyword)
    {
        var trimmed = keyword?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || !UserSettings.Load().EnableKeywordHistory)
            return;

        lock (Gate)
        {
            EnsureCacheNoLock();
            _entriesCache!.RemoveAll(x => x.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
            _entriesCache.Insert(0, trimmed);

            if (_entriesCache.Count > MaxEntries)
                _entriesCache.RemoveRange(MaxEntries, _entriesCache.Count - MaxEntries);

            SaveNoLock();
        }
    }

    public static void Delete(string keyword)
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            if (_entriesCache!.RemoveAll(x => x.Equals(keyword, StringComparison.OrdinalIgnoreCase)) > 0)
                SaveNoLock();
        }
    }

    public static IReadOnlyList<string> GetEntries()
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            return _entriesCache!.ToList();
        }
    }

    public static void SaveEntries(IEnumerable<string> entries)
    {
        lock (Gate)
        {
            _entriesCache = entries.Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxEntries)
                .ToList();

            SaveNoLock();
        }
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

    // Reproduces File.WriteAllLines' exact format so the atomic swap is byte-compatible with what
    // older builds wrote: every line followed by the platform newline, an empty list an empty file.
    private static string ToFileContent(List<string> entries) =>
        entries.Count == 0 ? string.Empty : string.Join(Environment.NewLine, entries) + Environment.NewLine;

    private static void EnsureCacheNoLock()
    {
        if (_entriesCache != null)
            return;

        _entriesCache = ReadEntriesNoLock();
    }

    private static List<string> ReadEntriesNoLock() => LoadFromFiles(HistoryPath, BackupPath);

    internal static List<string> LoadFromFiles(string mainPath, string backupPath)
    {
        // A missing main file is a fresh store: backups must not resurrect history after the file was
        // deliberately deleted. An existing file that cannot be read or parsed falls back to the
        // backup the atomic writer left behind, because an empty store would let the next save wipe
        // the user's history permanently.
        if (!File.Exists(mainPath))
            return new List<string>();

        return TryReadFile(mainPath) ?? TryReadFile(backupPath) ?? new List<string>();
    }

    /// <summary>Reads one history file into trimmed, deduped keywords; null when it cannot be read.</summary>
    internal static List<string>? TryReadFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return File.ReadLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxEntries)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Log($"[KeywordHistoryStore] Failed to read history from '{path}': {ex.Message}", LogLevel.Error);
            return null;
        }
    }
}
