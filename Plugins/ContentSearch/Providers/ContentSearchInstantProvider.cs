using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Providers;

/// <summary>
/// Instant result provider handling keyword-triggered full-text document content queries.
/// </summary>
public sealed class ContentSearchInstantProvider : IInstantResultProvider
{
    private const string PluginId = "Lertaro.Plugins.ContentSearch";
    private const string DefaultTrigger = "c";
    private static string? _cachedTrigger;

    static ContentSearchInstantProvider() => PluginSettingsService.SettingChanged += (pluginId, _) =>
    {
        if (string.Equals(pluginId, PluginId, StringComparison.OrdinalIgnoreCase))
            _cachedTrigger = null;
    };

    public string Name => TranslationService.Get("ContentSearch_ProviderName");
    public string Description => TranslationService.Get("ContentSearch_ProviderDesc");

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        var trigger = GetTriggerPrefix();
        if (string.IsNullOrEmpty(query) || !query.StartsWith(trigger, StringComparison.OrdinalIgnoreCase))
            yield break;

        var keyword = query[trigger.Length..].Trim();
        var db = ContentSearchPlugin.Database;
        var scheduler = ContentSearchPlugin.Scheduler;

        if (keyword.Length == 0)
        {
            var (totalFiles, _) = db.GetStats();
            yield return ContentSearchResultBuilder.CreatePlaceholderItem(totalFiles, scheduler.IsIndexing, scheduler.PendingCount);
            yield break;
        }

        var hits = db.SearchFts(keyword, 40);
        var merged = HybridSearchMerger.MergeRrf(hits, maxResults: 30);

        if (merged.Count == 0)
        {
            yield return ContentSearchResultBuilder.CreateNoResultsItem(keyword);
            yield break;
        }

        foreach (var item in ContentSearchResultBuilder.BuildResultItems(merged))
        {
            yield return item;
        }
    }

    public bool[]? GetHighlightMask(string text, string query)
    {
        var trigger = GetTriggerPrefix();
        if (string.IsNullOrEmpty(query) || !query.StartsWith(trigger, StringComparison.OrdinalIgnoreCase))
            return null;

        var keyword = query[trigger.Length..].Trim();
        if (keyword.Length == 0 || string.IsNullOrEmpty(text))
            return null;

        return FuzzyMatchService.GetHighlightMask(text, keyword) ?? new bool[text.Length];
    }

    private static string GetTriggerPrefix()
    {
        _cachedTrigger ??= PluginSettingsService.GetSetting(PluginId, "TriggerKeyword", DefaultTrigger).Trim();
        return (_cachedTrigger.Length > 0 ? _cachedTrigger : DefaultTrigger) + " ";
    }
}
