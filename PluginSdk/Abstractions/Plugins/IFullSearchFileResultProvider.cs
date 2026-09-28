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
    /// and the answer is painted once that search has settled. It must not touch UI state, and it is given
    /// the time a whole-index scan needs: a provider that answers in seconds delays its own rows, not the
    /// window.
    /// </remarks>
    IReadOnlyList<InstantResultItem> GetFileResults(string query, int limit);
}
