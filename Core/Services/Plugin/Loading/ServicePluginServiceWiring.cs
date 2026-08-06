using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Core.Services.Plugin.Loading;

// Bridges PluginSdk's static service delegates (TranslationService, PluginSettingsService) to their
// Core implementations for the elevated service/hook process. Kept separate from ServicePluginLoader,
// which only discovers and registers plugin assemblies -- neither of these delegates has anything to do
// with the scan itself.
internal static class ServicePluginServiceWiring
{
    public static void WireTranslations(IEnumerable<ITranslationProvider> translationProviders, string cultureName)
    {
        var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in translationProviders)
        {
            try
            {
                var dict = provider.GetTranslations(cultureName);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        translations[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[ServicePluginLoader] Failed to load translations from '{provider.Name}': {ex.Message}", LogLevel.Error);
            }
        }

        TranslationService.LookupFunc = key => translations.TryGetValue(key, out var val) ? val : $"[{key}]";
        TranslationService.CurrentCultureFunc = () =>
        {
            try
            {
                var preferred = UserSettings.Load().PreferredLanguage;
                return string.IsNullOrEmpty(preferred) ? cultureName : preferred;
            }
            catch { return cultureName; }
        };
    }

    public static void WirePluginSettings() => PluginSettingsService.GetSettingFunc = (pluginId, key, defVal) =>
                                                    {
                                                        try
                                                        {
                                                            var settings = UserSettings.Load();
                                                            if (settings.PluginSettings.TryGetValue(pluginId, out var dict))
                                                            {
                                                                if (dict.TryGetValue(key, out var val))
                                                                {
                                                                    if (val is System.Text.Json.JsonElement element)
                                                                    {
                                                                        if (element.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                                                                        if (element.ValueKind == System.Text.Json.JsonValueKind.False) return false;
                                                                        if (element.ValueKind == System.Text.Json.JsonValueKind.String) return element.GetString();
                                                                    }
                                                                    return val;
                                                                }
                                                            }
                                                        }
                                                        catch { }
                                                        return defVal;
                                                    };
}
