using System.Windows.Media;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.CoreExtensions.Actions;

public class LocateInExplorerAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_BuiltinGroup");

    public string DisplayName => TranslationService.Get("Action_LocateFolder");

    public string Description => TranslationService.Get("Action_LocateFolder_Desc");

    // Built-in hotkey; the search windows dispatch it through HotkeyActionTrigger instead of hardcoding.
    public string Hotkey => "Ctrl+Enter";

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8L10 4z M20 18H4V8h16v10z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count > 0 && results.All(r =>
        r != null && !string.IsNullOrEmpty(r.FullPath)
        && PathExistenceCache.Exists(r.FullPath));

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        foreach (var result in results)
            view.LocateInExplorerExternal(result.FullPath);
    }
}
