using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.QuickPanel;

// Quick panel source backed by the host's own favorites (FavoritesService, already wired to
// Lertaro.Core.UserSettings.Favorites) -- the same list the startup panel's Favorites tab shows, put
// where the panel can reach it without opening the search window first.
public class FavoritesTabProvider : IQuickPanelTabProvider
{
    public string Name => TranslationService.Get("QuickPanel_TabFavorites");

    // No timestamps and no cap: these are user-curated, usually few, and the order they arranged them in
    // IS the order they want. With every entry's Metadata left unknown, the panel's newest-first default
    // sorts none of them and shows them exactly as they come back.
    public Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ISearchResult> entries = FavoritesService.GetFavorites()
            .Where(favorite => !string.IsNullOrWhiteSpace(favorite.Path))
            .Select(favorite => (ISearchResult)new PanelResultItem(favorite.Path, favorite.Name))
            .ToList();

        return Task.FromResult(entries);
    }
}
