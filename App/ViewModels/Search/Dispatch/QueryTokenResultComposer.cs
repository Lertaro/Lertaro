namespace Lertaro.App.ViewModels.Search.Dispatch;

// Keeps the query-token display cap testable without coupling tests to the asynchronous search controller.
internal static class QueryTokenResultComposer
{
    internal const int DisplayLimit = 50;

    // `displayQuery` is the text the rows were searched with (a plugin's trigger word already taken off),
    // not the untouched box text: it goes into the "N more" row's label and its SearchQuery, and every
    // other row in this list carries the searched text -- the non-token path in SearchResultMapper already
    // did that, and doing it differently here made the same synthetic row read two ways depending on
    // whether a ":token" happened to be active.
    public static List<AppSearchResult> Compose(
        IReadOnlyList<AppSearchResult> instantRows,
        IReadOnlyList<AppSearchResult> processedFileRows,
        string displayQuery)
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
        SearchResultHelper.AddShowMoreResult(composed, displayQuery);
        return composed;
    }
}
