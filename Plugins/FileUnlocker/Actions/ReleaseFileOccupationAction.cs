using System.IO;
using System.Windows.Media;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Windows;

namespace Lertaro.Plugins.FileUnlocker.Actions;

public sealed class ReleaseFileOccupationAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("FileUnlocker_GroupName");

    public string DisplayName => TranslationService.Get("FileUnlocker_ActionName");

    public string Description => TranslationService.Get("FileUnlocker_ActionDesc");

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M6 2h9l5 5v15H6V2zm8 1.5V8h4.5L14 3.5zM8 11h10v1.5H8V11zm0 4h10v1.5H8V15zm0 4h7v1.5H8V19z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) =>
        results.Count == 1
        && !string.IsNullOrWhiteSpace(results[0].FullPath)
        && !results[0].IsDir
        && File.Exists(results[0].FullPath);

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        if (!CanExecute(results)) return;

        var path = results[0].FullPath;
        view.HideWindow();
        var window = new PluginWindow(
            TranslationService.Get("FileUnlocker_WindowTitle"),
            720,
            470,
            PluginWindowMode.Dialog);
        var content = new FileOccupationView(path);
        content.AttachFooter(window);
        window.ContentHostControl.Content = content;
        window.ShowDialog();
    }
}
