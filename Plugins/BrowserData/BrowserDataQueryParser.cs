namespace Lertaro.Plugins.BrowserData;

internal enum BrowserDataSearchScope
{
    None,
    Bookmarks,
    History
}

internal readonly record struct BrowserDataQuery(BrowserDataSearchScope Scope, string SearchTerm)
{
    public bool IsHandled => Scope != BrowserDataSearchScope.None;
}

// Keeps trigger parsing independent from result collection so bookmarks and history cannot share
// a result quota or accidentally enter each other's ranking pipeline.
internal static class BrowserDataQueryParser
{
    public static BrowserDataQuery Parse(string query, string bookmarkKeyword, string historyKeyword)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new BrowserDataQuery(BrowserDataSearchScope.None, string.Empty);

        if (TryStripKeyword(query, bookmarkKeyword, out var bookmarkTerm))
            return new BrowserDataQuery(BrowserDataSearchScope.Bookmarks, bookmarkTerm);
        if (TryStripKeyword(query, historyKeyword, out var historyTerm))
            return new BrowserDataQuery(BrowserDataSearchScope.History, historyTerm);
        return new BrowserDataQuery(BrowserDataSearchScope.None, string.Empty);
    }

    private static bool TryStripKeyword(string query, string keyword, out string searchTerm)
    {
        searchTerm = string.Empty;
        keyword = keyword.Trim();
        if (keyword.Length == 0)
            return false;

        var trimmed = query.Trim();
        if (string.Equals(trimmed, keyword, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!trimmed.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase))
            return false;

        searchTerm = trimmed.Substring(keyword.Length + 1).Trim();
        return true;
    }
}
