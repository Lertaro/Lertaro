using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ContentSearch.Providers;

/// <summary>
/// Provides embedded localization resources to the host application's TranslationManager.
/// </summary>
public sealed class ContentSearchTranslationProvider : ITranslationProvider
{
    public string Name => "ContentSearch Translation Provider";

    public IReadOnlyList<string> SupportedCultures =>
        TranslationService.GetSupportedCultures(typeof(ContentSearchTranslationProvider).Assembly);

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

            var dict = TranslationService.LoadEmbeddedTranslations(
                typeof(ContentSearchTranslationProvider).Assembly, cultureName, "Plugin");
            Cache[cultureName] = dict;
            return dict;
        }
    }
}
