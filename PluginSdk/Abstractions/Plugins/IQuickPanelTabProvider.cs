namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Contributes a tab beside the Quick Panel's workspaces. The host supplies result rows, icons,
/// opening behavior, and the actions menu.
/// </summary>
/// <remarks>
/// Return the complete list so the host can sort and cap it. Tabs load independently; the panel can
/// open while slower tabs are still loading. Honor the cancellation token even though the current
/// host leaves it uncancelled when hiding the panel.
///
/// Set <see cref="ISearchResult.Metadata"/>'s Modified value for newest-first sorting. Entries with
/// the default value retain provider order. An empty result produces no tab.
///
/// Tabs appear automatically when their plugin loads. Users can close them from the strip and
/// reopen them in Settings > Quick Panel. Disabling the component in Settings > Plugins prevents loading.
/// </remarks>
public interface IQuickPanelTabProvider : IPluginComponent
{
    /// <summary>The entries to show right now. Called each time the panel is summoned.</summary>
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
