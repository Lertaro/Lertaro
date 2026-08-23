using Flow.Launcher.Plugin;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;
using Lertaro.Plugins.FlowLauncherBridge.Engine.Community;

namespace Lertaro.Plugins.FlowLauncherBridge.Providers;

/// <summary>
/// Handles querying, filtering, and uninstalling loaded Flow.Launcher plugins.
/// </summary>
public static class FlowCommunityUninstallHelper
{
    public static IEnumerable<InstantResultItem> QueryInstalledPluginsForUninstall(
        FlowPluginHost host,
        string filter)
    {
        var installedPlugins = host.GetAllPlugins();

        var filtered = string.IsNullOrEmpty(filter)
            ? installedPlugins
            : installedPlugins.Where(p => MatchesPlugin(p, filter)).ToList();

        if (filtered.Count == 0)
        {
            return
            [
                new InstantResultItem
                {
                    Title = TranslationService.Get("FlowLauncherBridge_NoInstalledPluginsTitle"),
                    Description = TranslationService.Get("FlowLauncherBridge_NoInstalledPluginsDesc"),
                    ActionType = "None"
                }
            ];
        }

        var items = new List<InstantResultItem>();
        var uninstallLabel = TranslationService.Get("FlowLauncherBridge_Uninstall");

        foreach (var pair in filtered)
        {
            var meta = pair.Metadata;
            var lang = !string.IsNullOrEmpty(meta.Language) ? $"[{meta.Language}] " : string.Empty;
            var title = $"{meta.Name} v{meta.Version} [{uninstallLabel}]";
            var desc = $"{lang}{meta.Description}".Trim();

            items.Add(new InstantResultItem
            {
                Title = title,
                Description = desc,
                ActionType = "Execute",
                ActionArgument = meta.ID,
                OnExecute = () => Task.Run(async () =>
                    {
                        await FlowPluginInstaller.UninstallPluginAsync(meta, host);
                    })
            });
        }

        return items;
    }

    private static bool MatchesPlugin(PluginPair pair, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        if (IsMatch(filter, pair.Metadata.Name) || IsMatch(filter, pair.Metadata.Description) || IsMatch(filter, pair.Metadata.ActionKeyword))
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
}
