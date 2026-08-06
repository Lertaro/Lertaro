using System.IO;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Helpers;

/// <summary>Turns what a plugin hands back into a row this app can render.</summary>
/// <remarks>
/// A plugin cannot build an <see cref="AppSearchResult"/>: that type lives in this assembly and the SDK
/// deliberately exposes only <see cref="PluginSdk.Abstractions.ISearchResult"/>. So every surface that
/// takes entries from a provider has to map them, and there is one mapping rather than one per surface:
/// the startup panel and the quick panel show the same plugin's entries and have no business disagreeing
/// about what a web favorite or a WSL path looks like.
/// </remarks>
internal static class PluginResultMapper
{
    public static AppSearchResult ToUiResult(PluginSdk.Abstractions.ISearchResult item, int index)
    {
        // A web-address favorite isn't a real filesystem path: Path.GetDirectoryName mangles it (e.g.
        // "https://www.google.com" becomes "https:"), and there's no shell icon to look up for it either.
        var isWebUrl = FavoriteUrlHelper.IsWebUrl(item.FullPath);
        // FormatWslPath renders "\\wsl$\Ubuntu\..." as "WSL-Ubuntu:/...", the same format regular search
        // already shows for WSL results (see SearchResultHelper.GetParentDisplayText), so a WSL favorite/
        // history entry doesn't display differently just because it came through a plugin tab.
        var parentDir = isWebUrl ? item.FullPath : SearchResultHelper.FormatWslPath(Path.GetDirectoryName(item.FullPath) ?? string.Empty);
        var fullPath = item.FullPath;
        return new AppSearchResult
        {
            Name = item.Name,
            FullPath = fullPath,
            ParentDir = parentDir,
            ContextDirectory = item.ContextDirectory,
            IsDir = item.IsDir,
            Drive = string.IsNullOrEmpty(fullPath) ? string.Empty : (Path.GetPathRoot(fullPath) ?? string.Empty).TrimEnd('\\'),
            ResultKind = item.IsApplication ? "Application" : "File",
            Index = index,
            IconOverride = isWebUrl ? FavoriteUrlHelper.Icon : null,
            // "Application" results execute as an instant-result (PluginActionExecutor.TryExecute), not
            // through the "File" fallback path (FileExecutor.OpenFileOrFolder called by the search
            // window's own input handler): wire it up explicitly so it still actually launches instead
            // of silently no-op'ing into the default Copy-empty-string instant-result action.
            InstantResultOnExecute = item.IsApplication ? () => FileExecutor.OpenFileOrFolder(fullPath) : null,
            InstantResultActionArgument = item.IsApplication ? fullPath : string.Empty
        };
    }
}
