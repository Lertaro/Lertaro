using System.IO;
using System.Windows.Media;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.CoreExtensions.Actions;

public class TouchAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_GroupName_Cmd");

    public string DisplayName => TranslationService.Get("Action_Touch");

    public string Description => TranslationService.Get("Action_Touch_Desc");

    public IReadOnlyList<string> Keywords => new[] { "touch" };

    public IReadOnlyList<string> Parameters => new[] { "filename" };

    public bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => windowType == SearchWindowType.Inline;

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 12h-3v3h-2v-3H8v-2h3V9h2v3h3v2zm-3-5V3.5L18.5 9H13z",
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
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            if (!File.Exists(targetPath))
            {
                File.WriteAllBytes(targetPath, Array.Empty<byte>());
            }
        }
        catch
        {
        }
    }
}
