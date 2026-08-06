using System.Windows.Media;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.CoreExtensions.Actions;

public class CopyPathAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_BuiltinGroup");

    public string DisplayName => TranslationService.Get("Action_CopyPath");

    public string Description => TranslationService.Get("Action_CopyPath_Desc");

    // Built-in hotkey; the search windows dispatch it through HotkeyActionTrigger instead of hardcoding.
    public string Hotkey => "Ctrl+Shift+C";

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count > 0 && results.All(r => r != null && !string.IsNullOrEmpty(r.FullPath));

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        try
        {
            // All selected paths, newline-joined, in a single clipboard set.
            var text = string.Join(Environment.NewLine, results.Select(r => r.FullPath).Where(p => !string.IsNullOrEmpty(p)));
            System.Windows.Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            Logger.Log($"[CopyPathAction] Failed to copy path: {ex.Message}", LogLevel.Error);
        }
    }
}
