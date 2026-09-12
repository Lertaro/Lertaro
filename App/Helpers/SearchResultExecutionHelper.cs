using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Mapping;

namespace Lertaro.App.Helpers;

// Refreshes only the execution payload needed by an action when Enter races the search debounce.
internal static class SearchResultExecutionHelper
{
    internal static AppSearchResult? ResolveCurrent(AppSearchResult result, string query, bool isInlineWindow)
    {
        if ((!result.IsPluginSearchAction && !result.IsInstantResult)
            || string.Equals(result.SearchQuery, query, StringComparison.Ordinal))
            return result;

        var current = new List<AppSearchResult>();
        if (result.IsPluginSearchAction)
        {
            PluginSearchResultMapper.AddPluginSearchActionResults(current, query, result.ContextDirectory, isInlineWindow);
        }
        else
        {
            PluginSearchResultMapper.AddInstantResults(current, query, query, isInlineWindow);
        }

        return current.FirstOrDefault(candidate => SearchResultsReconciler.ItemsEqual(result, candidate));
    }
}
