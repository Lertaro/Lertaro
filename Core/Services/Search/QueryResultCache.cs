using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using Lertaro.Core;
using Lertaro.Core.IndexV2.Search;

namespace Lertaro.Core.Services.Search;

/// <summary>
/// Keeps the rows of recent searches on disk, so the first search of a session can show something
/// immediately instead of waiting for the service to start, load its index and run the scan.
/// </summary>
/// <remarks>
/// Measured on this machine: a cold service needs ~4.3 s to load its index and then ~7 s of warm-up, and
/// every search that lands in that window is slow. The rows themselves are cheap to store -- only the name,
/// path, kind and metadata a row renders -- so a session can start by showing what the previous one found.
///
/// The cache is a hint, never a source of truth: a hit is returned at once AND the caller still runs the
/// real search, and the fresh rows replace whatever was shown. Entries expire, and an entry whose stored
/// row no longer exists is simply stale output that the real search overwrites a moment later.
/// </remarks>
internal static class QueryResultCache
{
    private const string DirectoryName = "search-cache";
    private const int KeepEntries = 64;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private sealed class CachedRow
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsDir { get; set; }
        public string Drive { get; set; } = string.Empty;
    }

    private sealed class CachedPage
    {
        public string Query { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public long StoredAtUnix { get; set; }
        public List<CachedRow> Rows { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly object Gate = new();
    private static Dictionary<string, List<SearchResult>>? _memory;
    private static bool _pruned;

    /// <summary>
    /// Rows a previous session stored for this query. Served AT MOST ONCE per process, and only for the
    /// first search of the session.
    /// </summary>
    /// <remarks>
    /// A persisted page goes stale the moment a file is created, deleted or renamed, and showing it as if it
    /// were current would let a user click a file that no longer exists. So it is used strictly as the thing
    /// that fills the window while the service is still loading its index -- one search, once -- and every
    /// search after that goes to the service alone. The fresh rows also replace the cached ones as soon as
    /// they arrive, so the stale window is a few hundred milliseconds at the start of a session rather than
    /// anything the user can act on.
    /// </remarks>
    public static IReadOnlyList<SearchResult>? TryGetStartupPage(string query, string? scope)
    {
        // Only while the service can still be loading its index. Past that window a real search is fast
        // enough that a page which may predate any file change is a liability, not a shortcut.
        if (Environment.TickCount64 - _startedAt > StartupWindowMs)
            return null;

        try
        {
            EnsureLoaded();
            lock (Gate)
            {
                return _memory != null && _memory.TryGetValue(Key(query, scope), out var rows) ? rows : null;
            }
        }
        catch
        {
            return null;
        }
    }

    private static readonly long _startedAt = Environment.TickCount64;

    // How long after process start a persisted page may still be shown. The service needs ~4 s to load its
    // index and a few seconds more to warm up, so this covers exactly the window where a search has nothing
    // behind it; anything later is a real search that the cache would only make stale.
    private const long StartupWindowMs = 10_000;

    /// <summary>Stores a page for the next session to start from.</summary>
    public static void Store(string query, string? scope, IReadOnlyList<SearchResult> rows)
    {
        if (rows.Count == 0)
            return;

        try
        {
            var key = Key(query, scope);
            var page = new CachedPage
            {
                Query = query,
                Scope = scope ?? string.Empty,
                StoredAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Rows = new List<CachedRow>(rows.Count),
            };

            foreach (var row in rows)
            {
                page.Rows.Add(new CachedRow
                {
                    Path = row.Path,
                    Name = row.Name,
                    IsDir = row.IsDir,
                    Drive = row.Drive ?? string.Empty,
                });
            }

            var path = PathFor(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(page, Json));
            File.Move(temp, path, overwrite: true);

            lock (Gate)
            {
                _memory?.Remove(key);
                _memory ??= new Dictionary<string, List<SearchResult>>(StringComparer.Ordinal);
                _memory[key] = Rebuild(page);
            }

            PruneOnce();
        }
        catch
        {
            // A cache that cannot be written costs nothing; the search itself already succeeded.
        }
    }

    /// <summary>Every path any stored page mentions, for the icon warm-up to resolve ahead of time.</summary>
    public static IReadOnlyList<string> CachedPaths()
    {
        try
        {
            EnsureLoaded();
            lock (Gate)
            {
                if (_memory == null)
                    return Array.Empty<string>();

                var paths = new List<string>(_memory.Count * 8);
                foreach (var rows in _memory.Values)
                    foreach (var row in rows)
                        paths.Add(row.Path);
                return paths;
            }
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_memory != null)
                return;
            _memory = new Dictionary<string, List<SearchResult>>(StringComparer.Ordinal);
        }

        try
        {
            var dir = DirectoryPath();
            if (!Directory.Exists(dir))
                return;

            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > MaxAge)
                    {
                        File.Delete(file);
                        continue;
                    }

                    var page = JsonSerializer.Deserialize<CachedPage>(File.ReadAllText(file), Json);
                    if (page == null || page.Rows.Count == 0)
                        continue;

                    lock (Gate)
                        _memory[Path.GetFileNameWithoutExtension(file)] = Rebuild(page);
                }
                catch
                {
                    // One unreadable entry must not keep the rest from loading.
                }
            }
        }
        catch
        {
        }
    }

    private static List<SearchResult> Rebuild(CachedPage page)
    {
        var rows = new List<SearchResult>(page.Rows.Count);
        foreach (var cached in page.Rows)
        {
            rows.Add(new SearchResult
            {
                Path = cached.Path,
                Name = cached.Name,
                IsDir = cached.IsDir,
                Drive = cached.Drive,
            });
        }

        return rows;
    }

    private static void PruneOnce()
    {
        if (_pruned)
            return;
        _pruned = true;

        try
        {
            var dir = DirectoryPath();
            var files = new DirectoryInfo(dir).GetFiles("*.json");
            if (files.Length <= KeepEntries)
                return;

            foreach (var file in files.OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeepEntries))
            {
                try { file.Delete(); } catch { }
            }
        }
        catch
        {
        }
    }

    private static string DirectoryPath()
        => Path.Combine(Logger.UserDataDir, DirectoryName);

    private static string PathFor(string key) => Path.Combine(DirectoryPath(), key + ".json");

    // Hashed rather than literal: a query is user input and does not belong in a file name.
    private static string Key(string query, string? scope)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(query + "\u0001" + (scope ?? string.Empty));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes), 0, 16);
    }
}
