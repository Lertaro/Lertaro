using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Providers;

/// <summary>
/// Converts search engine hit records into user-facing InstantResultItem instances.
/// </summary>
public static class ContentSearchResultBuilder
{
    private const string DocumentSearchIcon = "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z";

    public static IEnumerable<InstantResultItem> BuildResultItems(IReadOnlyList<SearchHitItem> hits)
    {
        foreach (var hit in hits)
        {
            var desc = string.IsNullOrWhiteSpace(hit.DirectoryPath)
                ? hit.Snippet
                : $"{hit.Snippet}  ·  {hit.DirectoryPath}";

            yield return new InstantResultItem
            {
                Title = hit.FileName,
                Description = desc,
                ActionType = "Execute",
                ActionArgument = hit.FilePath
            };
        }
    }

    public static InstantResultItem CreatePlaceholderItem(int totalFiles, bool isIndexing, int pendingCount = 0)
    {
        string desc;
        if (isIndexing)
        {
            desc = pendingCount > 0
                ? TranslationService.Format("ContentSearch_IndexingWithRemainingDesc", totalFiles, pendingCount)
                : TranslationService.Format("ContentSearch_IndexingDesc", totalFiles);
        }
        else
        {
            desc = TranslationService.Format("ContentSearch_PlaceholderDesc", totalFiles);
        }

        return new InstantResultItem
        {
            Title = TranslationService.Get("ContentSearch_PlaceholderTitle"),
            Description = desc,
            IconData = DocumentSearchIcon,
            IconColor = "DefaultPluginIconColor",
            ActionType = "Execute",
            ActionArgument = GetPluginSettingsUri()
        };
    }

    public static InstantResultItem CreateNoResultsItem(string keyword) => new()
    {
        Title = TranslationService.Get("ContentSearch_NoResultsTitle"),
        Description = TranslationService.Format("ContentSearch_NoResultsDesc", keyword),
        IconData = DocumentSearchIcon,
        IconColor = "DefaultPluginIconColor",
        ActionType = "None",
        ActionArgument = string.Empty
    };

    private static string GetPluginSettingsUri()
    {
        try
        {
            var pluginName = TranslationService.Get("ContentSearch_PluginName");
            var configPrefix = $" › {pluginName} › ";
            var entries = SettingsSearchService.GetEntries();

            // Prioritize jumping directly to the plugin's Configuration tab
            var configEntry = entries.FirstOrDefault(e => e.Breadcrumb.Contains(configPrefix, StringComparison.OrdinalIgnoreCase));
            if (configEntry != null)
            {
                return $"lertaro://settings/entry/{configEntry.Index}";
            }

            var suffix = $" › {pluginName}";
            var pluginEntry = entries.FirstOrDefault(e => e.Breadcrumb.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (pluginEntry != null)
            {
                return $"lertaro://settings/entry/{pluginEntry.Index}";
            }
        }
        catch { }

        return "lertaro://settings/page/Plugins";
    }
}
