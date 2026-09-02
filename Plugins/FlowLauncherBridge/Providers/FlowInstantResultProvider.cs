using Flow.Launcher.Plugin;
using Lertaro.PluginSdk.Abstractions.Plugins;
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

    public string Name => PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_PluginName");

    private static string GetTriggerKeyword() => PluginSdk.Services.PluginSettingsService.GetSetting(
            "Lertaro.Plugins.FlowLauncherBridge",
            "TriggerKeyword",
            "flow");

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var trimmed = query.Trim();
        var keyword = GetTriggerKeyword();
        if (string.IsNullOrWhiteSpace(keyword))
            keyword = "flow";

        if (trimmed.Equals(keyword, StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase))
        {
            var filter = trimmed.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase)
                ? trimmed[(keyword.Length + 1)..].Trim()
                : string.Empty;

            if (filter.Equals("install", StringComparison.OrdinalIgnoreCase) || filter.StartsWith("install ", StringComparison.OrdinalIgnoreCase))
            {
                var listFilter = filter.StartsWith("install ", StringComparison.OrdinalIgnoreCase)
                    ? filter[8..].Trim()
                    : string.Empty;

                return FlowCommunityListHelper.QueryCommunityPlugins(_host, keyword, listFilter, trimmed);
            }

            if (filter.Equals("update", StringComparison.OrdinalIgnoreCase) || filter.StartsWith("update ", StringComparison.OrdinalIgnoreCase))
            {
                var updateFilter = filter.StartsWith("update ", StringComparison.OrdinalIgnoreCase)
                    ? filter[7..].Trim()
                    : string.Empty;

                return FlowCommunityUpdateHelper.QueryPluginUpdates(_host, keyword, updateFilter, trimmed);
            }

            if (filter.Equals("uninstall", StringComparison.OrdinalIgnoreCase) || filter.StartsWith("uninstall ", StringComparison.OrdinalIgnoreCase))
            {
                var uninstallFilter = filter.StartsWith("uninstall ", StringComparison.OrdinalIgnoreCase)
                    ? filter[10..].Trim()
                    : string.Empty;

                return FlowCommunityUninstallHelper.QueryInstalledPluginsForUninstall(_host, uninstallFilter);
            }

            var allPlugins = _host.GetAllPlugins();
            if (allPlugins.Count == 0 && string.IsNullOrEmpty(filter))
            {
                return
                [
                    new InstantResultItem
                    {
                        Title = PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_NoPluginsTitle"),
                        Description = PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_NoPluginsDesc"),
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
            var kwPrefix = PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_KeywordPrefix");
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

        if (PluginSdk.Services.FuzzyMatchService.IsMatchFunc != null)
            return PluginSdk.Services.FuzzyMatchService.IsMatch(pattern, text);

        return text.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    // How long GetInstantResults may block the calling (UI) thread on a dispatch. In-process Flow
    // plugins carry no internal timeout and script subprocesses cap at 15s -- without this bound a
    // slow or wedged plugin froze the search window on every keystroke.
    private const int DispatchTimeoutMs = 250;

    private IEnumerable<InstantResultItem> ExecuteDispatch(string q)
    {
        try
        {
            var results = _dispatcher
                .DispatchQueryAsync(q)
                .WaitAsync(TimeSpan.FromMilliseconds(DispatchTimeoutMs))
                .GetAwaiter()
                .GetResult();
            if (results == null || results.Count == 0)
                return [];

            return FlowResultMapper.MapToInstantResults(results, _host);
        }
        catch (TimeoutException)
        {
            // The abandoned dispatch keeps running in the background; its results are dropped and
            // the next keystroke re-dispatches. A short "querying" row replaces the frozen window
            // this path used to produce.
            return
            [
                new InstantResultItem
                {
                    Title = PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_QueryPendingTitle"),
                    Description = PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_QueryPendingDesc"),
                    ActionType = "None"
                }
            ];
        }
        catch
        {
            return [];
        }
    }

    public bool[]? GetHighlightMask(string text, string query) =>
        FlowHighlightHelper.GetHighlightMask(_host, GetTriggerKeyword(), text, query);
}
