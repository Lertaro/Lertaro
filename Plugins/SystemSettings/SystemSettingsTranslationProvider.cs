using System.Reflection;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.SystemSettings;

public class SystemSettingsTranslationProvider : ITranslationProvider
{
    public string Name => "SystemSettings Translation Provider";

    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(Assembly.GetExecutingAssembly());

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object LockObj = new();

    public IReadOnlyDictionary<string, string> GetTranslations(string cultureName)
    {
        lock (LockObj)
        {
            if (Cache.TryGetValue(cultureName, out var cached))
                return cached;

            var dict = TranslationService.LoadEmbeddedTranslations(Assembly.GetExecutingAssembly(), cultureName, "Plugin");
            Cache[cultureName] = dict;
            return dict;
        }
    }
}
