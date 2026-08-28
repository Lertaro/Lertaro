using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ContentSearch.Providers;

/// <summary>
/// Instant result provider handling keyword-triggered full-text document content queries.
/// </summary>
public sealed class ContentSearchInstantProvider : IInstantResultProvider
{
    private const string PluginId = "Lertaro.Plugins.ContentSearch";
    private const string DefaultTrigger = "cs";
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
            yield return ContentSearchResultBuilder.CreatePlaceholderItem(
                totalFiles,
                scheduler.IsIndexing,
                scheduler.PendingCount);
            yield break;
        }

        var ftsHits = db.SearchFts(keyword, 30);
        if (ftsHits.Count == 0)
        {
            yield return ContentSearchResultBuilder.CreateNoResultsItem(keyword);
            yield break;
        }

        foreach (var item in ContentSearchResultBuilder.BuildResultItems(ftsHits))
        {
            yield return item;
        }
    }

    public bool[]? GetHighlightMask(string text, string query)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text))
            return null;

        var triggerPrefix = GetTriggerPrefix();
        var trimmed = query.TrimStart();
        if (!trimmed.StartsWith(triggerPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var remainder = trimmed[triggerPrefix.Length..].Trim();
        if (remainder.Length == 0)
        {
            return new bool[text.Length];
        }

        return FuzzyMatchService.GetHighlightMask(text, remainder) ?? new bool[text.Length];
    }

    private static string GetTriggerKeyword()
    {
        _cachedTrigger ??= PluginSettingsService.GetSetting(PluginId, "TriggerKeyword", DefaultTrigger).Trim();
        return _cachedTrigger.Length > 0 ? _cachedTrigger : DefaultTrigger;
    }

    private static string GetTriggerPrefix() => GetTriggerKeyword() + " ";
}
