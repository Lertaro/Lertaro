using System.IO;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;
using Lertaro.PluginSdk.Helpers;

using Lertaro.Core.SearchIndex;
namespace Lertaro.App.ViewModels.Search;

public static class FavoriteSearchHelper
{
    // Display label for a favorite: explicit Name, else virtual-folder name / full URL / file name.
    private static string GetDisplayName(FavoriteItemSetting fav)
    {
        if (!string.IsNullOrWhiteSpace(fav.Name)) return fav.Name;
        var expanded = FavoritePathResolver.Expand(fav.Path);
        if (FavoritePathResolver.IsVirtualPath(expanded))
            return ShellPathHelper.GetVirtualFolderDisplayName(expanded, fav.Path);
        if (FavoriteUrlHelper.IsWebUrl(fav.Path))
            return fav.Path.Trim();
        try
        {
            var name = Path.GetFileName(expanded.TrimEnd('\\', '/'));
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        catch { }
        return fav.Path;
    }

    // The standard match+weight contract (FuzzyMatcher.ComputeBestMatch), matched against the display
    // name only -- matching against the raw path too used to let unrelated path segments (a parent
    // folder like "Program Files" fuzzy-contributing letters to an unrelated query) surface a favorite
    // with no real relevance to what was typed, and rank it above genuinely-matching favorites and other
    // results. A favorite is something the user deliberately named or picked; searching for it should
    // only need to match what it's actually called.
    internal static (bool IsMatch, double Weight) ComputeMatch(FavoriteItemSetting fav, string query)
        => FuzzyMatcher.ComputeBestMatch(query, GetDisplayName(fav));

    public static AppSearchResult CreateFavoriteUiResult(FavoriteItemSetting fav, string query, int index)
    {
        var resolved = FavoritePathResolver.Resolve(fav.Path);
        var isDir = FavoritePathResolver.IsVirtualPath(fav.Path) || Directory.Exists(resolved);
        var label = TranslationManager.Instance["Search_ResultFavorite"];

        var displayName = GetDisplayName(fav);

        return new AppSearchResult
        {
            Name = displayName,
            FullPath = resolved,
            ParentDir = "★ " + label,
            ContextDirectory = isDir ? resolved : (Path.GetDirectoryName(resolved) ?? resolved),
            IsDir = isDir,
            Drive = string.Empty,
            ResultKind = "File",
            Index = index,
            SearchQuery = query,
            // Web-address favorites have no shell icon; give them the globe icon.
            IconOverride = FavoriteUrlHelper.IsWebUrl(fav.Path) ? FavoriteUrlHelper.Icon : null
        };
    }

    public static List<AppSearchResult> CreateLaunchItems(
        IReadOnlyList<FavoriteItemSetting> favorites,
        Func<string, bool>? isAvailable = null)
    {
        isAvailable ??= path => FavoritePathResolver.IsPathAvailable(path);
        var items = new List<AppSearchResult>();

        foreach (var favorite in favorites)
        {
            if (!isAvailable(favorite.Path)) continue;
            items.Add(CreateFavoriteUiResult(favorite, string.Empty, items.Count));
        }

        return items;
    }
}
