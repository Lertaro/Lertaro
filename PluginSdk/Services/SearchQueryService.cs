namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Allows plugins to programmatically read or change the search query in the active search window.
/// </summary>
public static class SearchQueryService
{
    /// <summary>
    /// Delegate set by host application to update query text and optionally re-trigger search.
    /// </summary>
    public static Action<string, bool>? ChangeQueryFunc { get; set; }

    /// <summary>
    /// Modifies the current query in the active search window.
    /// </summary>
    /// <param name="query">The new query string.</param>
    /// <param name="requery">Whether to immediately re-run the search with the new query.</param>
    public static void ChangeQuery(string query, bool requery = false)
    {
        try
        {
            ChangeQueryFunc?.Invoke(query, requery);
        }
        catch { }
    }
}
