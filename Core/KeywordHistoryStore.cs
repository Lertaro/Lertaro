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
            File.WriteAllLines(HistoryPath, _entriesCache!);
        }
        catch (Exception ex)
        {
            Logger.Log($"[KeywordHistoryStore] Failed to write history: {ex.Message}", LogLevel.Error);
        }
    }

    private static void EnsureCacheNoLock()
    {
        if (_entriesCache != null)
            return;

        _entriesCache = ReadEntriesNoLock();
    }

    private static List<string> ReadEntriesNoLock()
    {
        if (!File.Exists(HistoryPath))
            return new List<string>();

        try
        {
            return File.ReadLines(HistoryPath)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxEntries)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Log($"[KeywordHistoryStore] Failed to read history: {ex.Message}", LogLevel.Error);
            return new List<string>();
        }
    }
}
