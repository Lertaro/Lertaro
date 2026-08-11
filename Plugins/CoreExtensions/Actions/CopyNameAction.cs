using System.Windows.Media;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Actions;

public class CopyNameAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_BuiltinGroup");
    public string DisplayName => TranslationService.Get("Action_CopyName");
    public string Description => TranslationService.Get("Action_CopyName_Desc");
    public string Hotkey => "Shift+C";

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M4 4h16v16H4V4zm2 3v2h12V7H6zm0 4v2h8v-2H6zm0 4v2h10v-2H6z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results)
        => results.Count > 0 && results.All(result => result != null && !string.IsNullOrEmpty(result.Name));

    internal static string BuildText(IReadOnlyList<ISearchResult> results)
        => string.Join(Environment.NewLine, results.Select(result => result.Name));

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        try
        {
            System.Windows.Clipboard.SetText(BuildText(results));
        }
        catch (Exception ex)
        {
            Logger.Log($"[CopyNameAction] Failed to copy name: {ex.Message}", LogLevel.Error);
        }
    }
}
