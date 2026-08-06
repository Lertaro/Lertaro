using System.Windows.Media;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Helpers;

using Lertaro.PluginSdk.Shell.FileOperations;
namespace Lertaro.Plugins.CoreExtensions.Actions;

public class DeleteFileAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_BuiltinGroup");

    public string DisplayName => TranslationService.Get("Action_Delete");

    public string Description => TranslationService.Get("Action_Delete_Desc");

    // No default hotkey, deliberately -- it used to be Delete, matching Explorer. The reasoning then was
    // that the native Recycle Bin confirmation is the real safeguard, not withholding the key; the
    // reports say otherwise, because here the key sits under a search box where Delete means "delete a
    // character" everywhere else. Still bindable in Settings.
    public string Hotkey => string.Empty;

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count > 0 && results.All(Exists);

    private static bool Exists(ISearchResult result)
    {
        if (result == null || string.IsNullOrEmpty(result.FullPath)) return false;
        return PathExistenceCache.Exists(result.FullPath);
    }

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        var paths = results.Where(Exists).Select(r => r.FullPath).ToArray();
        if (paths.Length == 0) return;
        ShellDeleteHelper.DeleteAsync(paths, permanent: false);
    }
}
