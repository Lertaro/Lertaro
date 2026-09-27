namespace Lertaro.Plugins.BrowserData;

using Lertaro.PluginSdk.Services;

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

    private static bool TryStripKeyword(string query, string keyword, out string searchTerm) =>
        // A bare "bb" activates and lists the bookmarks; the token rule is the host's own, shared with the
        // words it strips off the file search, so the two cannot disagree on a padded keyword.
        TriggerWord.TryMatch(query, keyword, out searchTerm);
}
