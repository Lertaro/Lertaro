using System.IO;
using System.Reflection;

namespace Lertaro.PluginSdk.Services;

/// <summary>
/// A decoupled service to provide runtime dynamic translations to plugins.
/// </summary>
public static class TranslationService
{
    /// <summary>
    /// Delegate function set by the main application to perform multi-language lookup.
    /// </summary>
    public static Func<string, string> LookupFunc { get; set; } = key => $"[{key}]";

    /// <summary>
    /// Delegate function set by the main application to attempt multi-language lookup, returning null if key is not found.
    /// </summary>
    public static Func<string, string?>? TryLookupFunc { get; set; }

    /// <summary>
    /// Delegate function set by the main application to expose the app's currently selected
    /// UI language (e.g. "zh-CN"), which is a user setting independent of the OS system locale.
    /// </summary>
    public static Func<string> CurrentCultureFunc { get; set; } = () => System.Globalization.CultureInfo.CurrentUICulture.Name;

    /// <summary>
    /// Event raised when the application's active UI culture changes.
    /// </summary>
    public static event Action<string>? CultureChanged;

    /// <summary>
    /// Notifies listeners that the application UI culture has changed.
    /// </summary>
    public static void NotifyCultureChanged(string cultureName)
    {
        try { CultureChanged?.Invoke(cultureName); } catch { }
    }

    /// <summary>
    /// Gets translation by key.
    /// </summary>
    public static string Get(string key) => LookupFunc(key);

    /// <summary>
    /// Attempts to retrieve a translation. Returns false and the raw key if no matching translation is found.
    /// </summary>
    public static bool TryGet(string key, out string result)
    {
        if (string.IsNullOrEmpty(key))
        {
            result = string.Empty;
            return false;
        }

        if (TryLookupFunc != null)
        {
            var val = TryLookupFunc(key);
            if (val != null)
            {
                result = val;
                return true;
            }
            result = key;
            return false;
        }

        var lookup = LookupFunc(key);
        if (lookup.StartsWith('[') && lookup.EndsWith(']') && lookup.Length == key.Length + 2 && lookup.Substring(1, key.Length) == key)
        {
            result = key;
            return false;
        }

        result = lookup;
        return true;
    }

    /// <summary>
    /// Gets the app's currently selected UI language/culture code (e.g. "zh-CN"), not the OS system locale.
    /// </summary>
    public static string GetCurrentCulture() => CurrentCultureFunc();

    /// <summary>
    /// Gets formatted translation by key.
    /// </summary>
    public static string Format(string key, params object[] args)
    {
        var fmt = LookupFunc(key);
        try
        {
            return string.Format(fmt, args);
        }
        catch
        {
            return fmt;
        }
    }

    /// <summary>
    /// Detects supported culture names by scanning embedded resource filenames under the prefix "Resources.Translations."
    /// </summary>
    public static IReadOnlyList<string> GetSupportedCultures(Assembly assembly)
    {
        var cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var prefix = "Resources.Translations.";
            var resourceNames = assembly.GetManifestResourceNames();
            foreach (var name in resourceNames)
            {
                var index = name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    var sub = name.Substring(index + prefix.Length);
                    var nextDot = sub.IndexOf('.');
                    if (nextDot > 0)
                    {
                        var cultureKey = sub.Substring(0, nextDot).Replace('_', '-');
                        if (cultureKey.Contains("-") && cultureKey.Length >= 5)
                        {
                            cultures.Add(cultureKey);
                        }
                    }
                }
            }
        }
        catch { }

        return cultures.ToList();
    }

    /// <summary>
    /// Loads translations from a JSON file embedded as resource in the specified assembly.
    /// Expected naming suffix: {cultureKey}.{typeName}.json or {cultureKey_with_underscore}.{typeName}.json
    /// </summary>
    public static Dictionary<string, string> LoadEmbeddedTranslations(Assembly assembly, string cultureKey, string typeName)
    {
        var target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cultureKeyUnderscore = cultureKey.Replace('-', '_');

        var suffix1 = $"{cultureKey}.{typeName}.json";
        var suffix2 = $"{cultureKeyUnderscore}.{typeName}.json";

        string? matchedResourceName = null;
        try
        {
            var resourceNames = assembly.GetManifestResourceNames();
            foreach (var name in resourceNames)
            {
                if (name.EndsWith(suffix1, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(suffix2, StringComparison.OrdinalIgnoreCase))
                {
                    matchedResourceName = name;
                    break;
                }
            }
        }
        catch { }

        if (string.IsNullOrEmpty(matchedResourceName)) return target;

        try
        {
            using var stream = assembly.GetManifestResourceStream(matchedResourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        target[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        catch { }

        return target;
    }
}
