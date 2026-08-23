using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Providers;

/// <summary>
/// Instant result provider feeding results from loaded Flow.Launcher plugins into Lertaro search.
/// </summary>
public class FlowInstantResultProvider : IInstantResultProvider
{
    private readonly FlowQueryDispatcher _dispatcher;

    public FlowInstantResultProvider() : this(FlowLauncherBridgePlugin.Dispatcher)
    {
    }

    public FlowInstantResultProvider(FlowQueryDispatcher dispatcher) => _dispatcher = dispatcher;

    public string Name => PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_PluginName");

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var trimmed = query.Trim();
        if (trimmed.Equals("flow", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("flow ", StringComparison.OrdinalIgnoreCase))
        {
            var plugins = FlowLauncherBridgePlugin.Host.GetAllPlugins();
            if (plugins.Count == 0)
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

            var items = new List<InstantResultItem>();
            var kwPrefix = PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_KeywordPrefix");
            var openSettingsHint = PluginSdk.Services.TranslationService.Get("FlowLauncherBridge_OpenSettingsHint");
            foreach (var pair in plugins)
            {
                var hasSettings = pair.Plugin is Flow.Launcher.Plugin.ISettingProvider;
                var kwList = pair.Metadata.ActionKeywords.Count > 0 ? pair.Metadata.ActionKeywords : [pair.Metadata.ActionKeyword];
                items.Add(new InstantResultItem
                {
                    Title = $"{pair.Metadata.Name} v{pair.Metadata.Version}",
                    Description = $"[{kwPrefix}: {string.Join(", ", kwList)}] {pair.Metadata.Description}" + (hasSettings ? $" · {openSettingsHint}" : ""),
                    ActionType = "Execute",
                    OnExecute = hasSettings ? () => FlowLauncherBridgePlugin.Host.OpenPluginSettings(pair.Metadata.ID) : null
                });
            }
            return items;
        }

        try
        {
            var results = _dispatcher.DispatchQueryAsync(query).GetAwaiter().GetResult();
            if (results == null || results.Count == 0)
                return [];

            return FlowResultMapper.MapToInstantResults(results);
        }
        catch
        {
            return [];
        }
    }
}
