using Lertaro.PluginSdk.Models;
namespace Lertaro.PluginSdk.Services;

public static class FavoritesService
{
    public static Func<IEnumerable<FavoriteItem>>? GetFavoritesFunc { get; set; }
    public static Func<string, bool>? IsFavoriteFunc { get; set; }
    public static Func<FavoriteItem, bool>? AddFavoriteFunc { get; set; }

    public static IEnumerable<FavoriteItem> GetFavorites() => GetFavoritesFunc?.Invoke() ?? Enumerable.Empty<FavoriteItem>();

    public static bool IsFavorite(string path) => IsFavoriteFunc?.Invoke(path)
        ?? GetFavorites().Any(favorite => string.Equals(favorite.Path, path, StringComparison.OrdinalIgnoreCase));

    public static bool TryAddFavorite(FavoriteItem favorite) => AddFavoriteFunc?.Invoke(favorite) ?? false;
}
