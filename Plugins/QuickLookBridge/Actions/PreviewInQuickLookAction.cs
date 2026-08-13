using System.IO;
using System.Windows.Media;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.QuickLookBridge.Actions;

// A manual "preview this" entry point alongside QuickLookPreviewProvider's automatic fallback --
// useful for previewing a file QuickLook can handle even when a higher-priority built-in provider
// already claims it (so the automatic path never reaches QuickLook for it). Shares the same pipe client,
// so availability is a real reachability check, not a separate filesystem probe.
public class PreviewInQuickLookAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("QuickLookBridge_GroupName");

    public string DisplayName => TranslationService.Get("QuickLookBridge_ActionName");

    public string Description => TranslationService.Get("QuickLookBridge_ActionDesc");

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M12,4.5C7,4.5 2.73,7.61 1,12C2.73,16.39 7,19.5 12,19.5C17,19.5 21.27,16.39 23,12C21.27,7.61 17,4.5 12,4.5ZM12,17A5,5 0 0,1 7,12A5,5 0 0,1 12,7A5,5 0 0,1 17,12A5,5 0 0,1 12,17ZM12,9A3,3 0 0,0 9,12A3,3 0 0,0 12,15A3,3 0 0,0 15,12A3,3 0 0,0 12,9Z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) =>
        results.Count == 1
        && !string.IsNullOrEmpty(results[0].FullPath)
        && (results[0].IsDir ? Directory.Exists(results[0].FullPath) : File.Exists(results[0].FullPath))
        && QuickLookPipeClient.IsAvailable();

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view) =>
        QuickLookPipeClient.TryInvokePreview(results[0].FullPath);
}
