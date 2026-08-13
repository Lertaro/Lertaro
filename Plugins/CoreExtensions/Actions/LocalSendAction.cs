using System.Windows.Media;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Actions;

public class LocalSendAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_BuiltinGroup");

    public string DisplayName => TranslationService.Get("Action_LocalSend");

    public string Description => TranslationService.Get("Action_LocalSend");

    public string Hotkey => string.Empty;

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5",
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
            var files = results.Where(PathExistenceCache.ExistsResult).Select(r => r.FullPath).ToList();
            var text = files.Count == 0 ? string.Join(Environment.NewLine, results.Select(r => r.Name).Where(t => !string.IsNullOrEmpty(t))) : null;

            LocalSendTransferService.OpenSendWindow(files, text);
        }
        catch (Exception ex)
        {
            Logger.Log($"[LocalSendAction] Failed to execute LocalSend action: {ex.Message}", LogLevel.Error);
        }
    }
}
