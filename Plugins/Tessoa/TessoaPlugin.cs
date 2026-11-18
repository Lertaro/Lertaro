using System.IO;
using System.Windows.Media;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.Tessoa;

public sealed class TessoaPlugin : IPlugin, IConfigurable, ITranslationProvider, IActionProvider
{
    internal const string Id = "Lertaro.Plugins.Tessoa";
    public string Name => "tessoa";
    public string Description => TranslationService.Get("Tessoa_Description");
    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(typeof(TessoaPlugin).Assembly);
    public IReadOnlyDictionary<string, string> GetTranslations(string cultureName) =>
        TranslationService.LoadEmbeddedTranslations(typeof(TessoaPlugin).Assembly, cultureName, "Plugin");

    public PluginConfigSchema GetConfigSchema() => new() { Fields = [
        new() { Key = "ExecutablePath", FieldType = ConfigFieldType.FilePath, LabelKey = "Tessoa_Path", DefaultValue = "" },
        new() { Key = "TriggerOnTyping", FieldType = ConfigFieldType.Boolean, LabelKey = "Tessoa_Typing", DefaultValue = false },
    ] };
    public IEnumerable<ISearchResultAction> GetActions() => [new OpenInTessoaAction()];
    public IEnumerable<IDynamicActionProvider> GetDynamicActionProviders() => [];
}

internal sealed class OpenInTessoaAction : ISearchResultAction
{
    public string GroupName => "tessoa";
    public string DisplayName => TranslationService.Get("Tessoa_Open");
    public ImageSource? Icon => null;
    public bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => false;
    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count == 1 && TessoaQuery.IsPath(results[0].FullPath)
        && (results[0].IsDir ? Directory.Exists(results[0].FullPath) : File.Exists(results[0].FullPath));

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        if (!CanExecute(results)) return;
        var path = results[0].FullPath;
        var reveal = !results[0].IsDir;
        _ = Task.Run(() =>
        {
            if (!TessoaNavigation.Navigate(null, path, reveal))
                PluginNotificationService.Show("tessoa", TranslationService.Get("Tessoa_Failed"));
        });
    }
}
