using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Dispatch;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;

using SearchWindowType = Lertaro.PluginSdk.Abstractions.SearchWindowType;
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
    // ResultTypeTriggerHandler.StripTrigger), AND a plugin's trigger word after that (see
    // PluginTriggerQuery), so every row built there -- including an instant provider's -- carries that
    // stripped text as its SearchQuery. BuildQuickResults still INVOKES an instant provider and a plugin
    // action with the raw text (they have to recognise their own word), which is why the rebuild below
    // hands them the box text as typed. Only a searchable-item row is re-derived from the stripped form,
    // because the mapper that produced it (SearchableItemMapper) is the only rebuild path that can
    // reproduce a row from it: re-deriving it while skipping the second of the two strips made Enter
    // rebuild "set 路径" against a row that says "路径", fail to find the row the user was pointing at,
    // and do nothing at all -- so both strips are applied here, in the order the pipeline applies them.
    internal static string ResolveSearchQuery(AppSearchResult result, string query, bool isInlineWindow,
        IReadOnlyDictionary<string, string> triggers) =>
        !isInlineWindow && !result.IsPluginSearchAction && result.SourceProvider is ISearchableItemProvider
            ? PluginTriggerQuery.Strip(
                SearchResultTypePriority.StripLeadingTrigger(query, triggers),
                SearchWindowType.Main)
            : query;
}
