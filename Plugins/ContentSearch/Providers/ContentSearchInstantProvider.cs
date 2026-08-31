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
            // "Indexed" means successfully indexed rows only: failed/skipped rows are not
            // searchable and must not be counted in the placeholder total.
            var indexedFiles = db.CountIndexedFiles();
            yield return ContentSearchResultBuilder.CreatePlaceholderItem(
                indexedFiles,
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

    /// <summary>
    /// True for exactly the query that shows the indexing placeholder ("cs" or "cs " with no
    /// search term). Used by the host's progress refresh to re-run only that query.
    /// </summary>
    internal static bool IsPlaceholderQuery(string query) =>
        !string.IsNullOrWhiteSpace(query) &&
        query.Trim().Equals(GetTriggerKeyword(), StringComparison.OrdinalIgnoreCase);
}
