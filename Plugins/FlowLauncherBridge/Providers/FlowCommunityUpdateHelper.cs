using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;
using Lertaro.Plugins.FlowLauncherBridge.Engine.Community;

namespace Lertaro.Plugins.FlowLauncherBridge.Providers;

/// <summary>
/// Handles checking and executing online updates for installed Flow.Launcher plugins.
/// </summary>
public static class FlowCommunityUpdateHelper
{
    public static IEnumerable<InstantResultItem> QueryPluginUpdates(
        FlowPluginHost host,
        string triggerKeyword,
        string updateFilter,
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
        var onlineMap = new Dictionary<string, FlowCommunityPlugin>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in cached)
        {
            if (!string.IsNullOrEmpty(p.ID)) onlineMap[p.ID] = p;
            if (!string.IsNullOrEmpty(p.Name) && !onlineMap.ContainsKey(p.Name)) onlineMap[p.Name] = p;
        }

        var updatableList = new List<(Flow.Launcher.Plugin.PluginPair Local, FlowCommunityPlugin Online)>();
        foreach (var local in installedPlugins)
        {
            FlowCommunityPlugin? online = null;
            if (!string.IsNullOrEmpty(local.Metadata.ID) && onlineMap.TryGetValue(local.Metadata.ID, out var byId))
                online = byId;
            else if (!string.IsNullOrEmpty(local.Metadata.Name) && onlineMap.TryGetValue(local.Metadata.Name, out var byName))
                online = byName;

            if (online != null && IsNewerVersion(online.Version, local.Metadata.Version))
            {
                if (string.IsNullOrEmpty(updateFilter) || MatchesUpdateFilter(online, local.Metadata.Name, updateFilter))
                {
                    updatableList.Add((local, online));
                }
            }
        }

        if (updatableList.Count == 0)
        {
            return
            [
                new InstantResultItem
                {
                    Title = TranslationService.Get("FlowLauncherBridge_NoUpdatesTitle"),
                    Description = TranslationService.Get("FlowLauncherBridge_NoUpdatesDesc"),
                    ActionType = "None"
                }
            ];
        }

        var items = new List<InstantResultItem>();
        foreach (var (local, online) in updatableList)
        {
            var isUpdating = FlowPluginInstaller.IsInstalling(online.ID);
            var statusPrefix = isUpdating ? $"[{TranslationService.Get("FlowLauncherBridge_Updating")}] " : string.Empty;
            var lang = !string.IsNullOrEmpty(online.Language) ? $"[{online.Language}] " : string.Empty;
            var author = !string.IsNullOrEmpty(online.Author) ? $"by {online.Author}" : string.Empty;

            var title = $"{online.Name} v{local.Metadata.Version} → v{online.Version} {author}".Trim();
            var desc = $"{statusPrefix}{lang}{online.Description}".Trim();

            items.Add(new InstantResultItem
            {
                Title = title,
                Description = desc,
                ActionType = "Execute",
                ActionArgument = online.UrlDownload,
                OnExecute = isUpdating
                    ? null
                    : () =>
                    {
                        Task.Run(async () =>
                        {
                            await FlowPluginInstaller.DownloadAndInstallPluginAsync(online, host);
                        });
                    }
            });
        }

        return items;
    }

    public static bool IsNewerVersion(string onlineVersionStr, string localVersionStr)
    {
        if (string.IsNullOrWhiteSpace(onlineVersionStr))
            return false;
        if (string.IsNullOrWhiteSpace(localVersionStr))
            return true;

        if (Version.TryParse(onlineVersionStr, out var onlineVer) && Version.TryParse(localVersionStr, out var localVer))
            return onlineVer > localVer;

        return string.Compare(onlineVersionStr, localVersionStr, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private static bool MatchesUpdateFilter(FlowCommunityPlugin plugin, string localName, string filter)
    {
        if (IsMatch(filter, localName) || IsMatch(filter, plugin.Name) || IsMatch(filter, plugin.Description) || IsMatch(filter, plugin.Author))
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
