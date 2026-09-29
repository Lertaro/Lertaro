namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// A plugin component that contributes real file/folder results to the full search window's
/// file-browser grid. Unlike IInstantResultProvider rows (which may be calculator, URL or other
/// text answers), every returned item must represent a real path so the grid's path/size/type
/// columns stay meaningful.
/// </summary>
public interface IFullSearchFileResultProvider : IPluginComponent
{
    /// <summary>
    /// Returns real file/folder results for the given query, or an empty list when this provider
    /// does not handle the query.
    /// </summary>
    /// <remarks>
    /// Called from a background thread while the full search window's own file search is still streaming,
    /// and its rows are painted as soon as it answers -- not once the search has settled. It must not touch
    /// UI state. A provider that answers in seconds delays its own rows, not the window.
    /// </remarks>
    IReadOnlyList<InstantResultItem> GetFileResults(string query, int limit);

    /// <summary>
    /// The same results, handed out one at a time as they are found.
    /// </summary>
    /// <remarks>
    /// Exists because a provider that walks a large index knows the first hit long before it knows the
    /// last, and the window can paint what it has. <see cref="GetFileResults"/> is the whole answer at
    /// once, so a provider that only implements that keeps working: this member's default body simply
    /// walks it, and the caller cannot tell the difference. Override it only if the provider can really
    /// produce hits incrementally -- and keep it side-effect free, on a background thread, like the member
    /// above.
    /// </remarks>
    IEnumerable<InstantResultItem> GetFileResultsStreamed(string query, int limit)
    {
        foreach (var item in GetFileResults(query, limit))
            yield return item;
    }
}
