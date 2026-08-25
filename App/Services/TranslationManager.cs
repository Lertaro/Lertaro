using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lertaro.Core;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.Services;

/// <summary>
/// Manages application-wide translation and i18n support.
/// Exposes an indexer for seamless WPF XAML data binding.
/// </summary>
public class TranslationManager : INotifyPropertyChanged
{
    private static readonly Lazy<TranslationManager> _instance = new(() => new TranslationManager());

    /// <summary>Gets the singleton instance of TranslationManager.</summary>
    public static TranslationManager Instance => _instance.Value;

    private string _currentCulture = "zh-CN";
    private readonly Dictionary<string, string> _translations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of TranslationManager and loads preferred language from user settings.
    /// ReloadTranslations() must be called explicitly after all plugins are loaded.
    /// </summary>
    public TranslationManager()
    {
        try
        {
            var settings = UserSettings.Load();
            if (!string.IsNullOrEmpty(settings.PreferredLanguage))
            {
                _currentCulture = settings.PreferredLanguage;
                try
                {
                    var ci = new System.Globalization.CultureInfo(_currentCulture);
                    System.Globalization.CultureInfo.CurrentUICulture = ci;
                    System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = ci;
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[TranslationManager] Failed to load preferred language: {ex.Message}", LogLevel.Warn);
        }
        // Do NOT call ReloadTranslations() here:
        // PluginManager may not be initialized yet, causing a recursive Lazy<T> exception.
    }

    /// <summary>Gets or sets the current culture name (e.g. "zh-CN" or "en-US").</summary>
    public string CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (_currentCulture != value)
            {
                _currentCulture = value;
                try
                {
                    var ci = new System.Globalization.CultureInfo(value);
                    System.Globalization.CultureInfo.CurrentUICulture = ci;
                    System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = ci;
                }
                catch { }
                ReloadTranslations();
                PluginSdk.Services.TranslationService.NotifyCultureChanged(value);
                OnPropertyChanged();
                OnPropertyChanged("Item[]");
            }
        }
    }

    /// <summary>
    /// Indexer to retrieve translated strings by key.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <returns>The translated string, or [key] as fallback.</returns>
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (_translations.TryGetValue(key, out var value))
                return value;
            return $"[{key}]";
        }
    }

    /// <summary>
    /// Attempts to retrieve a translation. Returns true if key was found in loaded dictionaries.
    /// </summary>
    public bool TryGet(string key, out string value)
    {
        if (string.IsNullOrEmpty(key))
        {
            value = string.Empty;
            return false;
        }
        return _translations.TryGetValue(key, out value!);
    }

    /// <summary>
    /// Refreshes all active translations by querying loaded translation plugins.
    /// </summary>
    public void ReloadTranslations()
    {
        _translations.Clear();

        // Load translations from registered plugins with fallbacks
        foreach (var provider in PluginManager.Instance.TranslationProviders)
        {
            try
            {
                // 1. Get first supported language translations (lowest priority fallback)
                IReadOnlyDictionary<string, string>? fallbackDict = null;
                if (provider.SupportedCultures != null && provider.SupportedCultures.Count > 0)
                {
                    fallbackDict = provider.GetTranslations(provider.SupportedCultures[0]);
                }

                // 2. Get English translations (middle priority fallback)
                var engDict = provider.GetTranslations("en-US");

                // 3. Get target translations (highest priority)
                var targetDict = provider.GetTranslations(_currentCulture);

                // Merge them in order of increasing priority so higher priorities overwrite lower ones
                if (fallbackDict != null)
                {
                    foreach (var kvp in fallbackDict)
                    {
                        _translations[kvp.Key] = kvp.Value;
                    }
                }
                if (engDict != null)
                {
                    foreach (var kvp in engDict)
                    {
                        _translations[kvp.Key] = kvp.Value;
                    }
                }
                if (targetDict != null)
                {
                    foreach (var kvp in targetDict)
                    {
                        _translations[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[TranslationManager] Failed to get translations from provider '{provider.Name}': {ex.Message}", LogLevel.Error);
            }
        }
    }

    /// <summary>
    /// Gets all unique culture codes supported by all loaded translation providers.
    /// </summary>
    public IEnumerable<string> GetAvailableCultures()
    {
        var cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in PluginManager.Instance.TranslationProviders)
        {
            foreach (var culture in provider.SupportedCultures)
            {
                cultures.Add(culture);
            }
        }
        return cultures;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
