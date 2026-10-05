using System.IO;
using System.Windows.Media;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

namespace Lertaro.Plugins.CoreExtensions.Actions;

public sealed class RunCommandHereAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Command_Name");
    public string DisplayName => TranslationService.Get("Command_RunHere");
    public string Description => DisplayName;
    public ImageSource? Icon => null;
    public bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => false;
    public bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) =>
        CommandSettings.ShowInQuickNav && results.Count == 1 && results[0].IsDir;
    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count == 1 && Directory.Exists(results[0].ContextDirectory);
    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view) =>
        CommandQuickNavigationProvider.Prompt(results[0].ContextDirectory);
}
