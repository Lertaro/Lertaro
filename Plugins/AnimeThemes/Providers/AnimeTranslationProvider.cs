using System.Reflection;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.AnimeThemes.Providers;

public class AnimeTranslationProvider : ITranslationProvider
{
    public string Name => "Anime Themes Translation Provider";

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

        // Load plugin-specific translations (Plugin.json)
        var pluginDict = TranslationService.LoadEmbeddedTranslations(Assembly.GetExecutingAssembly(), cultureKey, "Plugin");
        foreach (var kvp in pluginDict)
        {
            merged[kvp.Key] = kvp.Value;
        }

        return merged;
    }
}
