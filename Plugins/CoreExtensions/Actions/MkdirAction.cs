using System.IO;
using System.Windows.Media;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.CoreExtensions.Actions;

public class MkdirAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_GroupName_Cmd");

    public string DisplayName => TranslationService.Get("Action_Mkdir");

    public string Description => TranslationService.Get("Action_Mkdir_Desc");

    public IReadOnlyList<string> Keywords => new[] { "mkdir" };

    public IReadOnlyList<string> Parameters => new[] { "foldername" };

    public bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => windowType == SearchWindowType.Inline;

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M20 6h-8l-2-2H4c-1.11 0-1.99.89-1.99 2L2 18c0 1.11.89 2 2 2h16c1.11 0 2-.89 2-2V8c0-1.11-.89-2-2-2zm-1 8h-3v3h-2v-3h-3v-2h3V9h2v3h3v2z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count == 1 && !string.IsNullOrWhiteSpace(results[0].ContextDirectory) && Directory.Exists(results[0].ContextDirectory);

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        var result = results[0];
        if (string.IsNullOrWhiteSpace(result.FullPath))
        {
            return;
        }

        try
        {
            var targetPath = Path.Combine(result.ContextDirectory, result.FullPath.Trim());
            Directory.CreateDirectory(targetPath);
        }
        catch
        {
        }
    }
}
