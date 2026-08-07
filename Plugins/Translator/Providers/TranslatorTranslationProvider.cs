using System.Reflection;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.Translator.Providers;

public sealed class TranslatorTranslationProvider : ITranslationProvider
{
    public string Name => "Translator Plugin Translation Provider";

    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(Assembly.GetExecutingAssembly());

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> GetTranslations(string cultureName)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(cultureName, out var translations))
            {
                translations = TranslationService.LoadEmbeddedTranslations(Assembly.GetExecutingAssembly(), cultureName, "Plugin");
                Cache[cultureName] = translations;
            }

            return translations;
        }
    }
}
