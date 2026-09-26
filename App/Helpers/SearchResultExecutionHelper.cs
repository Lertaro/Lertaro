using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Helpers;

// Refreshes only the execution payload needed by an action when Enter races the search debounce.
internal static class SearchResultExecutionHelper
{
    internal static AppSearchResult? ResolveCurrent(AppSearchResult result, string query, bool isInlineWindow)
    {
        if (!result.IsPluginSearchAction && !result.IsInstantResult)
            return result;

        if (string.Equals(result.SearchQuery, query, StringComparison.Ordinal))
            return result;

        // Only now consult the trigger table: the box text may still carry a per-type trigger the
        // mappers never saw, which would make an up-to-date row look stale (see ResolveSearchQuery).
        var searchQuery = ResolveSearchQuery(result, query, isInlineWindow, UserSettings.Load().ResultTypeTriggers);
        if (string.Equals(result.SearchQuery, searchQuery, StringComparison.Ordinal))
            return result;

        var current = new List<AppSearchResult>();
        if (result.IsPluginSearchAction)
        {
            PluginSearchResultMapper.AddPluginSearchActionResults(current, searchQuery, result.ContextDirectory, isInlineWindow);
        }
        else if (result.SourceProvider is ISearchableItemProvider)
        {
            // Rows from an ISearchableItemProvider (system settings, Start Menu apps, ...) are not
            // instant-provider output: asking AddInstantResults alone found nothing for them, which
            // made Enter do nothing on a row that was plainly sitting on screen.
            current.AddRange(SearchableItemMapper.CollectSearchableItemResults(searchQuery, isInlineWindow)
                .Select(candidate => candidate.Result));
        }
        else
        {
            PluginSearchResultMapper.AddInstantResults(current, searchQuery, searchQuery, isInlineWindow);
        }

        return current.FirstOrDefault(candidate => SearchResultsReconciler.ItemsEqual(result, candidate));
    }

    // The quick window strips a configured per-type trigger before the mappers run (see
    // ResultTypeTriggerHandler.StripTrigger), so a searchable-item row's SearchQuery holds the stripped
    // text while the search box still holds the raw one. Only these rows get the stripped form:
    // BuildQuickResults deliberately hands instant-result plugins the raw text, a plugin action parses
    // its own tokens out of it, and the inline window has no concept of a trigger at all.
    internal static string ResolveSearchQuery(AppSearchResult result, string query, bool isInlineWindow,
        IReadOnlyDictionary<string, string> triggers) =>
        !isInlineWindow && !result.IsPluginSearchAction && result.SourceProvider is ISearchableItemProvider
            ? SearchResultTypePriority.StripLeadingTrigger(query, triggers)
            : query;
}
