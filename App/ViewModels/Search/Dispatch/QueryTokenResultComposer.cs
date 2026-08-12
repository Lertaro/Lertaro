namespace Lertaro.App.ViewModels.Search.Dispatch;

// Keeps the query-token display cap testable without coupling tests to the asynchronous search controller.
internal static class QueryTokenResultComposer
{
    internal const int DisplayLimit = 50;

    public static List<AppSearchResult> Compose(
        IReadOnlyList<AppSearchResult> instantRows,
        IReadOnlyList<AppSearchResult> processedFileRows,
        string query)
    {
        var composed = new List<AppSearchResult>(instantRows.Count + processedFileRows.Count + 1);
        composed.AddRange(instantRows);

        if (processedFileRows.Count + instantRows.Count <= DisplayLimit)
        {
            composed.AddRange(processedFileRows);
            return composed;
        }

        // Instant results remain visible; files use the rest of the same 50-result budget as a normal
        // quick search, followed by the existing action that opens the complete result window.
        var visibleFileCount = Math.Max(0, DisplayLimit - instantRows.Count);
        composed.AddRange(processedFileRows.Take(visibleFileCount));
        SearchResultHelper.AddShowMoreResult(composed, query);
        return composed;
    }
}
