namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Contributes a tab to the Quick Panel -- the floating panel docked over whatever window is in front.
/// The tab carries this provider's whole list, and the host renders the entries through its own result
/// rows (icons, opening, the actions menu all come for free).
/// </summary>
/// <remarks>
/// A tab, not a section of somebody else's. This was briefly the other way round -- a plugin's list was
/// added to a workspace the way a folder is -- and the settings made the mistake obvious: Favorites had
/// to be ticked into every workspace one at a time, and was missing from every new one. A workspace is a
/// set of folders assembled for one kind of work; a plugin's list is a whole collection, orthogonal to
/// whichever set of folders the user happens to be looking at. So it sits beside the workspaces in the
/// strip, reachable by the same number keys, rather than inside one of them.
///
/// Deliberately not a streaming shape: this panel orders and caps a tab's entries as a set (newest
/// first, or by name, at most so many), so it cannot show half of one without re-sorting on every
/// arrival. That costs nothing in latency -- every tab loads on its
/// own task and the panel opens on the first one to arrive, so a provider that has to go and look delays
/// only its own tab. Honour the token all the same: it is cancelled when the panel closes.
///
/// Fill in <see cref="ISearchResult.Metadata"/>'s Modified where the source knows one and the default
/// newest-first order uses it; leave it at its default and the entries keep the order they were returned
/// in. A provider that returns nothing gets no tab, and there is nothing to configure for that.
///
/// The tab is there as soon as the plugin is, unlike a folder, which the user goes and adds. It can be
/// closed from the strip and reopened under Settings > Quick Panel -- a separate question from disabling
/// the component itself under Settings > Plugins, which stops it being loaded at all.
/// </remarks>
public interface IQuickPanelTabProvider : IPluginComponent
{
    /// <summary>The entries to show right now. Called each time the panel is summoned.</summary>
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
