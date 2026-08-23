using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;
using Lertaro.Plugins.FlowLauncherBridge.Engine.Community;

namespace Lertaro.Plugins.FlowLauncherBridge.Providers;

/// <summary>
/// Handles querying, filtering, and displaying community plugins from the online manifest.
/// Split out from FlowInstantResultProvider to keep files modular and under line limits.
/// </summary>
public static class FlowCommunityListHelper
{
    public static IEnumerable<InstantResultItem> QueryCommunityPlugins(
        FlowPluginHost host,
        string triggerKeyword,
        string listFilter,
        string fullQuery)
    {
        var cached = FlowCommunityManifestService.GetCachedPlugins();
        if (cached == null || cached.Count == 0)
        {
            FlowCommunityManifestService.TriggerBackgroundFetch(triggerKeyword, fullQuery);
            return
            [
                new InstantResultItem
                {
                    Title = TranslationService.Get("FlowLauncherBridge_CommunityLoadingTitle"),
                    Description = TranslationService.Get("FlowLauncherBridge_CommunityLoadingDesc"),
                    ActionType = "None"
                }
            ];
        }

        var installedPlugins = host.GetAllPlugins();
        var installedMap = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in installedPlugins)
        {
            if (!string.IsNullOrEmpty(p.Metadata.ID)) installedMap.Add(p.Metadata.ID);
            if (!string.IsNullOrEmpty(p.Metadata.Name)) installedMap.Add(p.Metadata.Name);
        }

        var uninstalled = cached.Where(p =>
            (string.IsNullOrEmpty(p.ID) || !installedMap.Contains(p.ID)) &&
            (string.IsNullOrEmpty(p.Name) || !installedMap.Contains(p.Name)));

        var filtered = (string.IsNullOrEmpty(listFilter)
            ? uninstalled
            : uninstalled.Where(p => MatchesCommunityPlugin(p, listFilter))).ToList();

        if (filtered.Count == 0)
        {
            return
            [
                new InstantResultItem
                {
                    Title = TranslationService.Get("FlowLauncherBridge_NoCommunityPlugins"),
                    Description = listFilter,
                    ActionType = "None"
                }
            ];
        }

        var items = new List<InstantResultItem>();

        foreach (var plugin in filtered)
        {
            var isInstalling = FlowPluginInstaller.IsInstalling(plugin.ID);
            var statusPrefix = isInstalling ? $"[{TranslationService.Get("FlowLauncherBridge_Installing")}] " : string.Empty;
            var lang = !string.IsNullOrEmpty(plugin.Language) ? $"[{plugin.Language}] " : string.Empty;
            var author = !string.IsNullOrEmpty(plugin.Author) ? $"by {plugin.Author}" : string.Empty;

            var title = $"{plugin.Name} v{plugin.Version} {author}".Trim();
            var desc = $"{statusPrefix}{lang}{plugin.Description}".Trim();

            items.Add(new InstantResultItem
            {
                Title = title,
                Description = desc,
                ActionType = "Execute",
                ActionArgument = plugin.UrlDownload,
                OnExecute = isInstalling
                    ? null
                    : () =>
                    {
                        Task.Run(async () =>
                        {
                            await FlowPluginInstaller.DownloadAndInstallPluginAsync(plugin, host);
                        });
                    }
            });
        }

        return items;
    }

    private static bool MatchesCommunityPlugin(FlowCommunityPlugin plugin, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        if (IsMatch(filter, plugin.Name))
            return true;

        if (IsMatch(filter, plugin.Description))
            return true;

        if (IsMatch(filter, plugin.Author))
            return true;

        if (IsMatch(filter, plugin.Language))
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
}
