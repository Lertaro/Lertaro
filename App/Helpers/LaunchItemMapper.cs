using System.IO;
using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.Helpers;

internal static class LaunchItemMapper
{
    public static AppSearchResult ToUiResult(QuickLaunchItemSetting item, int index)
    {
        var resolved = FavoritePathResolver.Resolve(item.Path);
        var isDir = FavoritePathResolver.IsVirtualPath(item.Path) || Directory.Exists(resolved);
        var displayName = string.IsNullOrWhiteSpace(item.Name) ? FavoritePathResolver.GetDisplayName(item.Path) : item.Name;
        return new AppSearchResult
        {
            Name = displayName,
            FullPath = resolved,
            ParentDir = TranslationManager.Instance["QuickLaunch_ManualSource"],
            ContextDirectory = isDir ? resolved : Path.GetDirectoryName(resolved) ?? resolved,
            IsDir = isDir,
            Drive = string.Empty,
            ResultKind = "File",
            Index = index,
            IconOverride = FavoriteUrlHelper.IsWebUrl(item.Path) ? FavoriteUrlHelper.Icon : null
        };
    }
}
