using System.Reflection;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.Theming;

/// <summary>
/// Core implementation of the translation provider, managing Chinese and English localized resources.
/// Loaded dynamically from nested folder structures Resources/Translations/{lang}/{type}.json
/// </summary>
public class CoreTranslationProvider : ITranslationProvider
{
    public string Name => "Core Translation Provider";

    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(Assembly.GetExecutingAssembly());

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object LockObj = new();

    public IReadOnlyDictionary<string, string> GetTranslations(string cultureName)
    {
        lock (LockObj)
        {
            if (Cache.TryGetValue(cultureName, out var cached))
            {
                return cached;
            }

            var translations = LoadMergedTranslations(cultureName);
            Cache[cultureName] = translations;
            return translations;
        }
    }

    private static Dictionary<string, string> LoadMergedTranslations(string cultureKey)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. Load main app translations
        var appDict = TranslationService.LoadEmbeddedTranslations(Assembly.GetExecutingAssembly(), cultureKey, "App");
        foreach (var kvp in appDict)
        {
            merged[kvp.Key] = kvp.Value;
        }

        // 2. Load plugin-specific translations
        var pluginDict = TranslationService.LoadEmbeddedTranslations(Assembly.GetExecutingAssembly(), cultureKey, "Plugin");
        foreach (var kvp in pluginDict)
        {
            merged[kvp.Key] = kvp.Value;
        }

        return merged;
    }
}
