using Lertaro.App.Helpers;

namespace Lertaro.App.ViewModels.Settings;

// Split out purely to keep FavoritesSettingsViewModel under the repository's per-file line limit.
// This support owns only the batch path-addition operation for that view model.
internal static class FavoritesSettingsPathSupport
{
    public static void AddPaths(FavoritesSettingsViewModel owner, IEnumerable<string> paths)
    {
        var existing = owner.Items
            .Select(item => FavoritePathResolver.NormalizeForComparison(item.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in paths)
        {
            var path = rawPath.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path) || !FavoritePathResolver.IsPathAvailable(path))
                continue;
            if (!existing.Add(FavoritePathResolver.NormalizeForComparison(path)))
                continue;

            owner.Items.Add(new FavoriteItemViewModel
            {
                Name = FavoritePathResolver.GetDisplayName(path),
                Path = path
            });
        }

        owner.NewName = string.Empty;
        owner.NewPath = string.Empty;
    }
}
