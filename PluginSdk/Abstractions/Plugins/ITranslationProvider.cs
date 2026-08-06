namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Provides translations for internationalization (i18n).
/// </summary>
public interface ITranslationProvider : IPluginComponent
{

    /// <summary>
    /// Gets the list of culture codes this provider supports (e.g. ["zh-CN", "en-US"]).
    /// Used by the settings UI to display which languages the provider covers.
    /// </summary>
    IReadOnlyList<string> SupportedCultures => Array.Empty<string>();

    /// <summary>
    /// Retrieves translation key-value pairs for the specified culture.
    /// </summary>
    /// <param name="cultureName">The culture name, e.g. "zh-CN" or "en-US".</param>
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
