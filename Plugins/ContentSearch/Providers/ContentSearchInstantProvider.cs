using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ContentSearch.Providers;

/// <summary>
/// Instant result provider handling keyword-triggered full-text document content queries.
/// </summary>
public sealed class ContentSearchInstantProvider : IInstantResultProvider, IFullSearchFileResultProvider
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
    // The word the user types to invoke this provider, published for the host so it can strip it before
    // matching/highlighting file names. Read live from the plugin's own settings: the host never keeps a
    // copy, and changing the word in Settings takes effect on the next keystroke.
    public IReadOnlyList<string> QueryTriggerKeywords => [GetTriggerKeyword()];
    public string Description => TranslationService.Get("ContentSearch_ProviderDesc");

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (!TryGetSearchTerm(query, out var keyword))
            yield break;

        var db = ContentSearchPlugin.Database;
        var scheduler = ContentSearchPlugin.Scheduler;
        if (db == null || scheduler == null)
            yield break;

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

    public IReadOnlyList<InstantResultItem> GetFileResults(string query, int limit) => GetFileResultsStreamed(query, limit).ToList();

    /// <summary>
    /// The walk out of the content index, one row per hit, handed over as the index reaches it. The whole
    /// answer would otherwise arrive only after a short term has read every indexed document, which on a
    /// real corpus is seconds -- and seconds of an empty grid is the complaint this exists to answer.
    /// </summary>
    public IEnumerable<InstantResultItem> GetFileResultsStreamed(string query, int limit)
    {
        // Only a real content-search keyword ("cs xxx") contributes hits; the bare "cs " placeholder has
        // no file rows to show there.
        if (!TryGetSearchTerm(query, out var keyword) || keyword.Length == 0)
            yield break;

        var database = ContentSearchPlugin.Database;
        if (database == null)
            yield break;

        foreach (var hit in database.SearchFtsStreamed(keyword, limit))
            yield return ContentSearchResultBuilder.CreateResultItem(hit);
    }

    public bool[]? GetHighlightMask(string text, string query)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text))
            return null;

        // Null when the word is not on the front of it -- that is this contract's way of declining, after
        // which the host's own matcher highlights the same term the row was searched with anyway.
        if (!TryGetSearchTerm(query, out var term))
            return null;

        var mask = new bool[text.Length];
        if (term.Length == 0)
            return mask;

        return FuzzyMatchService.GetHighlightMask(text, term) ?? mask;
    }

    private static string GetTriggerKeyword()
    {
        _cachedTrigger ??= TriggerWord.Normalize(PluginSettingsService.GetSetting(PluginId, "TriggerKeyword", DefaultTrigger));
        return _cachedTrigger.Length > 0 ? _cachedTrigger : DefaultTrigger;
    }

    // "cs 报告 :jpg" is the word "cs" carrying the term "报告". Providers are handed the untouched box
    // text so they can still recognise their own word -- and with it the host's trailing token syntax,
    // which the full-text index would otherwise search FOR ("a document containing ':jpg'"). So the
    // tokens come off here, before the term is used as a search term or as highlight text.
    private static bool TryGetSearchTerm(string query, out string term) =>
        TriggerWord.TryMatchInvoked(SearchQueryService.StripQueryTokens(query), GetTriggerKeyword(), out term);

    /// <summary>
    /// True for exactly the query that shows the indexing placeholder ("cs" or "cs " with no
    /// search term). Used by the host's progress refresh to re-run only that query.
    /// </summary>
    internal static bool IsPlaceholderQuery(string query) =>
        !string.IsNullOrWhiteSpace(query) &&
        query.Trim().Equals(GetTriggerKeyword(), StringComparison.OrdinalIgnoreCase);
}
