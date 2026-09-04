using System.Reflection;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.AudioDeviceSelector;

public sealed class AudioDeviceSelectorTranslationProvider : ITranslationProvider
{
    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    public string Name => "Audio Device Selector Translation Provider";
    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(Assembly.GetExecutingAssembly());

    public IReadOnlyDictionary<string, string> GetTranslations(string cultureName)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(cultureName, out var translations))
                return translations;

            translations = TranslationService.LoadEmbeddedTranslations(Assembly.GetExecutingAssembly(), cultureName, "Plugin");
            Cache[cultureName] = translations;
            return translations;
        }
    }
}
