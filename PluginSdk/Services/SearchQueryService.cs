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
    /// Delegate set by the host: takes query text and returns it without the host's trailing
    /// "&lt;keyword&gt; :a,b,c" token syntax. Providers are handed the untouched box text on purpose, so a
    /// trigger word they own is still there to recognise -- and with it the token suffix, which a
    /// calculator can ignore but anything that searches the remainder as TEXT must not search WITH it.
    /// </summary>
    public static Func<string, string>? StripQueryTokensFunc { get; set; }

    /// <summary>
    /// The query without the host's trailing token syntax; unchanged when no delegate is wired.
    /// </summary>
    public static string StripQueryTokens(string query) => StripQueryTokensFunc?.Invoke(query) ?? query;

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
