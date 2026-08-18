using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.DirectoryOpus.Scripts;

namespace Lertaro.Plugins.DirectoryOpus;

public class DirectoryOpusPlugin : IPlugin, IConfigurable, ITranslationProvider
{
    public string Name => "Directory Opus";
    public string Description => TranslationService.Get("DirectoryOpus_PluginDesc");

    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(System.Reflection.Assembly.GetExecutingAssembly());

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object LockObj = new();

    public IReadOnlyDictionary<string, string> GetTranslations(string cultureName)
    {
        lock (LockObj)
        {
            if (!Cache.TryGetValue(cultureName, out var translations))
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                translations = TranslationService.LoadEmbeddedTranslations(assembly, cultureName, "Plugin");
                Cache[cultureName] = translations;
            }

            if (cultureName.Equals(TranslationService.GetCurrentCulture(), StringComparison.OrdinalIgnoreCase))
                DirectoryOpusSizeColumnInstaller.EnsureInstalled(System.Reflection.Assembly.GetExecutingAssembly(), translations);

            return translations;
        }
    }

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "EnableInlineSearch",
                LabelKey = "Plugins_DirectoryOpus_EnableInlineSearch",
                DescriptionKey = "Plugins_DirectoryOpus_EnableInlineSearchDesc",
                FieldType = ConfigFieldType.Boolean,
                DefaultValue = true
            },
            new PluginConfigField
            {
                Key = "EnableQuickNav",
                LabelKey = "Plugins_DirectoryOpus_EnableQuickNav",
                DescriptionKey = "Plugins_DirectoryOpus_EnableQuickNavDesc",
                FieldType = ConfigFieldType.Boolean,
                DefaultValue = true
            }
        }
    };
}
