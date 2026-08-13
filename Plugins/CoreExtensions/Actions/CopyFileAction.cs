using System.Windows.Media;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.CoreExtensions.Actions;

public class CopyFileAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_BuiltinGroup");

    public string DisplayName => TranslationService.Get("Action_Copy");

    public string Description => TranslationService.Get("Action_Copy_Desc");

    // Built-in hotkey; the search windows dispatch it through HotkeyActionTrigger instead of hardcoding.
    public string Hotkey => "Ctrl+C";

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count > 0 && results.All(Exists);

    private static bool Exists(ISearchResult result)
    {
        if (result == null || string.IsNullOrEmpty(result.FullPath)) return false;
        return PathExistenceCache.ExistsResult(result);
    }

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        try
        {
            var fileList = new System.Collections.Specialized.StringCollection();
            foreach (var r in results)
                if (Exists(r)) fileList.Add(r.FullPath);
            System.Windows.Clipboard.SetFileDropList(fileList);
        }
        catch (Exception ex)
        {
            Logger.Log($"[CopyFileAction] Failed to copy file: {ex.Message}", LogLevel.Error);
        }
    }
}
