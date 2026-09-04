namespace Lertaro.PluginSdk.Services;

/// <summary>
/// A decoupled service allowing plugins to register custom directories for global indexing and real-time monitoring.
/// </summary>
public static class DirectoryIndexerService
{
    /// <summary>
    /// Delegate set by the host application to handle directory registration.
    /// Parameters: (pluginId, directoryPath, recursive, filterPattern)
    /// </summary>
    public static Action<string, string, bool, string>? RegisterDirectoryAction { get; set; }

    /// <summary>
    /// Delegate set by the host application to clear directory registrations for a plugin.
    /// Parameters: (pluginId)
    /// </summary>
    public static Action<string>? UnregisterDirectoriesAction { get; set; }

    /// <summary>
    /// Delegate set by the host application to perform target directory search.
    /// Parameters: (pluginId, query, token)
    /// </summary>
    public static Func<string, string, CancellationToken, Task<List<Abstractions.ISearchResult>>>? SearchPluginDirectoriesFunc { get; set; }

    /// <summary>
    /// Delegate set by the host application to list a directory out of the file index.
    /// Parameters: (directoryPath, recursive, filterPattern, limit, token)
    /// </summary>
    public static Func<string, bool, string, int, CancellationToken, IAsyncEnumerable<Abstractions.ISearchResult>>? EnumerateDirectoryFunc { get; set; }

    private static readonly Dictionary<string, List<Action<IReadOnlyList<string>>>> _watchers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Calls <paramref name="onChanged"/> whenever something changes under a directory registered by
    /// <paramref name="pluginId"/>. Dispose the returned handle to stop listening.
    /// </summary>
    /// <remarks>
    /// Per registrant, not a broadcast: a subscriber hears about its own directories and nothing else,
    /// so there is no id to compare and no chance of acting on somebody else's change by forgetting
    /// to. The host has already worked out whose directories a change falls under (it is what the
    /// registrations are for) -- making every plugin repeat that test was asking each of them to
    /// re-derive an answer the host was holding.
    ///
    /// Raised on a background thread, already debounced: a bulk copy is one call once the directory
    /// settles, not one per file. Marshal to your own thread if you need to.
    /// </remarks>
    public static IDisposable WatchDirectories(string pluginId, Action onChanged)
        => WatchDirectories(pluginId, _ => onChanged());

    /// <summary>
    /// Calls <paramref name="onChanged"/> with the directories affected by the change. An empty list
    /// means the host could not identify a narrower scope and the plugin should refresh broadly.
    /// Dispose the returned handle to stop listening.
    /// </summary>
    public static IDisposable WatchDirectories(string pluginId, Action<IReadOnlyList<string>> onChanged)
    {
        lock (_watchers)
        {
            if (!_watchers.TryGetValue(pluginId, out var handlers))
                _watchers[pluginId] = handlers = new List<Action<IReadOnlyList<string>>>();
            handlers.Add(onChanged);
        }
        return new Subscription(pluginId, onChanged);
    }

    /// <summary>
    /// Tells the plugin that registered them that its directories changed. Host application only.
    /// </summary>
    public static void NotifyDirectoryChanged(string pluginId)
        => NotifyDirectoryChanged(pluginId, Array.Empty<string>());

    /// <summary>Notifies a plugin about the directories affected by a watched change.</summary>
    public static void NotifyDirectoryChanged(string pluginId, IReadOnlyList<string> changedDirectories)
    {
        Action<IReadOnlyList<string>>[] handlers;
        lock (_watchers)
        {
            if (!_watchers.TryGetValue(pluginId, out var registered) || registered.Count == 0)
                return;
            // Copied before leaving the lock: a handler is free to unsubscribe itself while running,
            // and a plugin being torn down mid-notification is the ordinary case for one that does.
            handlers = registered.ToArray();
        }

        foreach (var handler in handlers)
        {
            try
            { handler(changedDirectories); }
            catch (Exception ex)
            {
                Logger.Log($"[DirectoryIndexerService] A '{pluginId}' change handler threw: {ex.Message}", LogLevel.Error);
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly string _pluginId;
        private Action<IReadOnlyList<string>>? _handler;

        public Subscription(string pluginId, Action<IReadOnlyList<string>> handler)
        {
            _pluginId = pluginId;
            _handler = handler;
        }

        public void Dispose()
        {
            var handler = Interlocked.Exchange(ref _handler, null);
            if (handler == null)
                return;

            lock (_watchers)
            {
                if (_watchers.TryGetValue(_pluginId, out var handlers) && handlers.Remove(handler) && handlers.Count == 0)
                    _watchers.Remove(_pluginId);
            }
        }
    }

    /// <summary>
    /// Registers a directory to be indexed and monitored by the host system (service or app manager).
    /// </summary>
    public static void RegisterDirectory(string pluginId, string directoryPath, bool recursive = true, string filterPattern = "*") => RegisterDirectoryAction?.Invoke(pluginId, directoryPath, recursive, filterPattern);

    /// <summary>
    /// Unregisters all directories registered by the specified plugin.
    /// </summary>
    public static void UnregisterDirectories(string pluginId) => UnregisterDirectoriesAction?.Invoke(pluginId);

    /// <summary>
    /// Searches within all directories registered by this plugin, honouring the <c>recursive</c> and
    /// <c>filterPattern</c> each was registered with. Answered from the host's file index wherever one
    /// covers the directory (see <see cref="EnumerateDirectoryAsync"/> for that routing and for what
    /// the pattern means); matching is the host's own fuzzy, alias-aware matching, and an empty query
    /// keeps everything. Directories are returned alongside files -- drop them with
    /// <see cref="Abstractions.ISearchResult.IsDir"/> if unwanted.
    /// </summary>
    public static async Task<List<Abstractions.ISearchResult>> SearchDirectoriesAsync(string pluginId, string query, CancellationToken token = default)
    {
        if (SearchPluginDirectoriesFunc == null) return new List<Abstractions.ISearchResult>();
        return await SearchPluginDirectoriesFunc(pluginId, query, token);
    }

    /// <summary>
    /// Lists a directory's contents from the host's file index instead of the filesystem: for a local
    /// drive the host indexes (the usual case) this costs no disk I/O at all, which is the whole point
    /// of preferring it over <c>Directory.EnumerateFileSystemEntries</c>. There is deliberately NO
    /// live filesystem fallback here: a directory no index covers (an unconfigured network share, a
    /// folder on a drive indexing is disabled for) yields an empty sequence, so callers must keep
    /// their directories index-covered. The search path -- not this enumeration -- is where live
    /// scanning of uncovered locations exists.
    /// <para>
    /// Results stream as they are produced. <paramref name="filterPattern"/> is a FILE pattern -- one
    /// or more Win32 wildcards separated by ';' or ',' (e.g. <c>"*.exe;*.lnk"</c>, default <c>"*"</c>)
    /// -- so directories are always returned whatever it says (drop them with
    /// <see cref="Abstractions.ISearchResult.IsDir"/> if unwanted), and
    /// <paramref name="recursive"/> descends regardless of what it selects. Hidden and system
    /// entries are never returned (the same always-on filter the host applies to every search result),
    /// though a hidden directory is still descended into -- only the entry's own attributes count. The
    /// user's exclusion settings are not applied, since the caller named one exact directory to look at.
    /// </para>
    /// <para>
    /// <paramref name="limit"/> caps how many entries are returned (0 = no cap). Worth setting for a
    /// recursive listing of a large tree: <c>EnumerateDirectoryAsync(@"C:\", recursive: true)</c> is a
    /// legitimate request for every single entry on the volume, and it will deliver exactly that.
    /// </para>
    /// </summary>
    public static IAsyncEnumerable<Abstractions.ISearchResult> EnumerateDirectoryAsync(string directoryPath, bool recursive = false, string filterPattern = "*", int limit = 0, CancellationToken token = default)
        => EnumerateDirectoryFunc?.Invoke(directoryPath, recursive, filterPattern, limit, token) ?? EmptyResults();

    private static async IAsyncEnumerable<Abstractions.ISearchResult> EmptyResults()
    {
        await Task.CompletedTask;
        yield break;
    }
}
