using Lertaro.PluginSdk.Models;
namespace Lertaro.PluginSdk.Services;

public static class FavoritesService
{
    public static Func<IEnumerable<FavoriteItem>>? GetFavoritesFunc { get; set; }

    public static IEnumerable<FavoriteItem> GetFavorites() => GetFavoritesFunc?.Invoke() ?? Enumerable.Empty<FavoriteItem>();
}
