namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Lets an async instant-result provider (one whose data isn't ready synchronously within
/// GetInstantResults, e.g. because it depends on a background network fetch) ask the host to
/// re-run any currently active search once that data becomes available.
/// </summary>
public static class SearchRefreshService
{
    /// <summary>
    /// Delegate function set by the main application. Given a predicate over a search's current
    /// query text, re-runs every active search view whose query text satisfies it.
    /// </summary>
    public static Action<Func<string, bool>>? RefreshMatchingFunc { get; set; }

    /// <summary>
    /// Requests that any active search whose current query text satisfies <paramref name="queryMatches"/>
    /// be re-run. No-op if the host hasn't wired up <see cref="RefreshMatchingFunc"/>.
    /// </summary>
    public static void RefreshIfMatches(Func<string, bool> queryMatches)
    {
        try
        {
            RefreshMatchingFunc?.Invoke(queryMatches);
        }
        catch { }
    }
}
