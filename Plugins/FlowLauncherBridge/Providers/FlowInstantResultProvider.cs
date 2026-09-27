using System.Collections.Concurrent;
using Flow.Launcher.Plugin;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Providers;

/// <summary>
/// Instant result provider feeding results from loaded Flow.Launcher plugins into Lertaro search.
/// </summary>
public class FlowInstantResultProvider : IInstantResultProvider
{
    private readonly FlowQueryDispatcher _dispatcher;
    private readonly FlowPluginHost _host;

    public FlowInstantResultProvider() : this(FlowLauncherBridgePlugin.Dispatcher, FlowLauncherBridgePlugin.Host)
    {
    }

    public FlowInstantResultProvider(FlowQueryDispatcher dispatcher) : this(dispatcher, FlowLauncherBridgePlugin.Host)
    {
    }

    public FlowInstantResultProvider(FlowQueryDispatcher dispatcher, FlowPluginHost host)
    {
        _dispatcher = dispatcher;
        _host = host;
    }

    public string Name => TranslationService.Get("FlowLauncherBridge_PluginName");
    // Every word the user can type to reach a Flow plugin, published for the host so it strips them before
    // matching/highlighting file names: the bridge's own word ("flow"), plus each loaded Flow plugin's own
    // ActionKeyword -- typing one of those at the front dispatches straight to that plugin (see
    // FlowQueryDispatcher.ParseQuery), so it is a trigger word by the same right, and before this published
    // them the file list beside such a query was still matched and highlighted against "gh lertaro". Read
    // live from the plugin's own settings and registry: the host never keeps a copy, and changing a word in
    // Settings takes effect on the next keystroke.
    public IReadOnlyList<string> QueryTriggerKeywords
    {
        get
        {
            var words = new List<string> { GetTriggerKeyword() };
            if (_host == null)
                return words;

            try
            {
                var seen = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
                foreach (var (keyword, plugins) in _host.KeywordPlugins)
                {
                    // A keyword only disabled plugins answer to is not offered at dispatch time
                    // (FlowQueryDispatcher.GetTargetPlugins filters them out), so it must not be stripped
                    // either -- the two sides have to agree on what the word means.
                    if (string.IsNullOrWhiteSpace(keyword) || !plugins.Any(pair => !pair.Metadata.Disabled))
                        continue;

                    if (seen.Add(keyword))
                        words.Add(keyword);
                }
            }
            catch (Exception ex)
            {
                // A half-registered plugin list must not cost the user the bridge's own word -- a
                // QueryTriggerKeywords that throws is a provider the host skips entirely.
                PluginSdk.Logger.Log($"[FlowLauncherBridge] Reading action keywords failed: {ex.Message}", PluginSdk.LogLevel.Error);
            }

            return words;
        }
    }

    private static string GetTriggerKeyword()
    {
        var value = PluginSettingsService.GetSetting(
            "Lertaro.Plugins.FlowLauncherBridge", "TriggerKeyword", "flow");
        return TriggerWord.Normalize(value) is { Length: > 0 } word ? word : "flow";
    }

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        var trimmed = query.Trim();
        var keyword = GetTriggerKeyword();

        // "flow" alone lists the loaded plugins; "flow <filter>" filters that list, and the sub-commands
        // below parse their own argument the same way. Every one of them was a hand-rolled offset into the
        // text ("install " is 8 characters, so filter[8..]) -- TriggerWord parses the same shape everywhere
        // else in the search box, including the sub-commands here.
        if (TriggerWord.TryMatch(query, keyword, out var filter))
        {
            if (TriggerWord.TryMatch(filter, "install", out var listFilter))
                return FlowCommunityListHelper.QueryCommunityPlugins(_host, keyword, listFilter, trimmed);

            if (TriggerWord.TryMatch(filter, "update", out var updateFilter))
                return FlowCommunityUpdateHelper.QueryPluginUpdates(_host, keyword, updateFilter, trimmed);

            if (TriggerWord.TryMatch(filter, "uninstall", out var uninstallFilter))
                return FlowCommunityUninstallHelper.QueryInstalledPluginsForUninstall(_host, uninstallFilter);

            var allPlugins = _host.GetAllPlugins();
            if (allPlugins.Count == 0 && string.IsNullOrEmpty(filter))
            {
                return
                [
                    new InstantResultItem
                    {
                        Title = TranslationService.Get("FlowLauncherBridge_NoPluginsTitle"),
                        Description = TranslationService.Get("FlowLauncherBridge_NoPluginsDesc"),
                        ActionType = "None"
                    }
                ];
            }

            var plugins = string.IsNullOrEmpty(filter)
                ? allPlugins
                : allPlugins.Where(p => MatchesPlugin(p, filter)).ToList();

            if (plugins.Count == 0)
                return [];

            var items = new List<InstantResultItem>();
            var kwPrefix = TranslationService.Get("FlowLauncherBridge_KeywordPrefix");
            foreach (var pair in plugins)
            {
                var kwSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(pair.Metadata.ActionKeyword)) kwSet.Add(pair.Metadata.ActionKeyword);
                if (pair.Metadata.ActionKeywords != null)
                {
                    foreach (var kw in pair.Metadata.ActionKeywords)
                        if (!string.IsNullOrWhiteSpace(kw)) kwSet.Add(kw);
                }
                var kwList = kwSet.Count > 0 ? kwSet : [pair.Metadata.ActionKeyword];
                var item = new InstantResultItem
                {
                    Title = $"{pair.Metadata.Name} v{pair.Metadata.Version}",
                    Description = $"[{kwPrefix}: {string.Join(", ", kwList)}] {pair.Metadata.Description}",
                    ActionType = "None",
                    OnExecuteFunc = () => _host.OpenPluginSettings(pair.Metadata.ID)
                };
                FlowPluginIconHelper.AttachPluginIcon(item, pair.Metadata);
                items.Add(item);
            }
            return items;
        }

        return ExecuteDispatch(query);
    }

    private static bool MatchesPlugin(PluginPair pair, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        if (IsMatch(filter, pair.Metadata.Name))
            return true;

        if (IsMatch(filter, pair.Metadata.Description))
            return true;

        if (IsMatch(filter, pair.Metadata.ActionKeyword))
            return true;

        if (pair.Metadata.ActionKeywords != null && pair.Metadata.ActionKeywords.Any(kw => IsMatch(filter, kw)))
            return true;

        return false;
    }

    private static bool IsMatch(string pattern, string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        if (FuzzyMatchService.IsMatchFunc != null)
            return FuzzyMatchService.IsMatch(pattern, text);

        return text.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private readonly ConcurrentDictionary<string, (List<Result> Results, DateTimeOffset Timestamp)> _queryCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<List<Result>>> _inFlightQueries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _refreshScheduledQueries = new(StringComparer.Ordinal);
    private const int DispatchTimeoutMs = 300;

    private IEnumerable<InstantResultItem> ExecuteDispatch(string q)
    {
        if (_queryCache.TryGetValue(q, out var cached) &&
            (DateTimeOffset.UtcNow - cached.Timestamp).TotalSeconds < 15)
        {
            if (cached.Results.Count == 0)
                return [];
            return FlowResultMapper.MapToInstantResults(cached.Results, _host);
        }

        var task = _inFlightQueries.GetOrAdd(q, queryKey => Task.Run(async () =>
        {
            try
            {
                var res = await _dispatcher.DispatchQueryAsync(queryKey).ConfigureAwait(false);
                var list = res ?? [];
                _queryCache[queryKey] = (list, DateTimeOffset.UtcNow);

                if (_queryCache.Count > 50)
                {
                    var expired = _queryCache
                        .Where(kv => (DateTimeOffset.UtcNow - kv.Value.Timestamp).TotalSeconds > 30)
                        .Select(kv => kv.Key)
                        .ToList();
                    foreach (var key in expired)
                        _queryCache.TryRemove(key, out _);
                }

                return list;
            }
            finally
            {
                _inFlightQueries.TryRemove(queryKey, out _);
            }
        }));

        try
        {
            if (task.Wait(DispatchTimeoutMs))
            {
                var results = task.GetAwaiter().GetResult();
                if (results.Count == 0)
                    return [];
                return FlowResultMapper.MapToInstantResults(results, _host);
            }
        }
        catch
        {
            return [];
        }

        // The synchronous wait gave up, so a "query pending" placeholder is what is actually on screen.
        // Ask the host to re-run this query ONCE the dispatch lands (the cache write above then serves it
        // instantly). Deliberately attached here rather than called from inside the task body: doing it
        // unconditionally there re-ran the whole search even on the fast path that already returned these
        // results to the caller -- and Flow usually answers within the timeout, so that fired on every
        // keystroke, doubling the search cost for nothing.
        // Several callers can time out while they are all waiting on this same in-flight dispatch. Only the
        // first one needs to arrange the host refresh; otherwise one completed dispatch produces one refresh
        // per timed-out caller and re-runs the same query repeatedly.
        if (_refreshScheduledQueries.TryAdd(q, 0))
        {
            _ = task.ContinueWith(
                completed =>
                {
                    try
                    {
                        // Only a successful dispatch has written the cache entry this refresh exists to
                        // surface. A faulted/cancelled one left the cache empty, and asking the host to
                        // re-run then would dispatch again and re-timeout, spinning on a search that can
                        // never resolve.
                        if (completed.IsCompletedSuccessfully)
                        {
                            SearchRefreshService.RefreshIfMatches(current =>
                                string.Equals(current?.Trim(), q.Trim(), StringComparison.OrdinalIgnoreCase));
                        }
                    }
                    finally
                    {
                        _refreshScheduledQueries.TryRemove(q, out _);
                    }
                },
                TaskScheduler.Default);
        }

        return
        [
            new InstantResultItem
            {
                Title = TranslationService.Get("FlowLauncherBridge_QueryPendingTitle"),
                Description = TranslationService.Get("FlowLauncherBridge_QueryPendingDesc"),
                ActionType = "None"
            }
        ];
    }

    public bool[]? GetHighlightMask(string text, string query) =>
        FlowHighlightHelper.GetHighlightMask(_host, GetTriggerKeyword(), text, query);
}
