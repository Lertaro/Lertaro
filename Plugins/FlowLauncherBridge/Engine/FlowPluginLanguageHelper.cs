using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Xml.Linq;
using Flow.Launcher.Plugin;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Discovers and loads plugin-specific XAML language resource dictionaries (Languages/*.xaml)
/// matching the current user interface culture into WPF resources and an in-memory translation cache.
/// </summary>
public static class FlowPluginLanguageHelper
{
    private static readonly ConcurrentDictionary<string, string> TranslationCache = new(StringComparer.OrdinalIgnoreCase);

    public static string GetTranslation(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        if (TranslationCache.TryGetValue(key, out var cached))
            return cached;

        if (Application.Current != null)
        {
            try
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    if (Application.Current.TryFindResource(key) is string resStr)
                    {
                        TranslationCache[key] = resStr;
                        return resStr;
                    }
                }
            }
            catch { }
        }

        return key;
    }

    public static CultureInfo GetEffectiveCulture()
    {
        var cultureName = PluginSdk.Services.TranslationService.GetCurrentCulture();
        if (!string.IsNullOrEmpty(cultureName))
        {
            try { return new CultureInfo(cultureName); } catch { }
        }
        return CultureInfo.CurrentUICulture;
    }

    public static void LoadPluginLanguage(string pluginDirectory, ResourceDictionary? targetDictionary = null, CultureInfo? culture = null)
    {
        if (string.IsNullOrEmpty(pluginDirectory)) return;

        var languagesDir = Path.Combine(pluginDirectory, "Languages");
        if (!Directory.Exists(languagesDir)) return;

        culture ??= GetEffectiveCulture();
        var languageFile = FindLanguageFile(languagesDir, culture);
        if (string.IsNullOrEmpty(languageFile) || !File.Exists(languageFile)) return;

        PopulateTranslationCache(languageFile);

        if (targetDictionary != null)
        {
            try
            {
                for (var i = targetDictionary.MergedDictionaries.Count - 1; i >= 0; i--)
                {
                    var md = targetDictionary.MergedDictionaries[i];
                    if (md.Source != null && md.Source.IsAbsoluteUri && md.Source.LocalPath.StartsWith(languagesDir, StringComparison.OrdinalIgnoreCase))
                    {
                        targetDictionary.MergedDictionaries.RemoveAt(i);
                    }
                }

                var dict = new ResourceDictionary { Source = new Uri(languageFile, UriKind.Absolute) };
                targetDictionary.MergedDictionaries.Add(dict);
            }
            catch { }
        }
    }

    public static void UpdatePluginsCulture(IEnumerable<PluginPair> plugins, string cultureName)
    {
        CultureInfo culture;
        try { culture = new CultureInfo(cultureName); }
        catch { culture = GetEffectiveCulture(); }

        foreach (var pair in plugins)
        {
            try
            {
                if (Application.Current != null)
                {
                    LoadPluginLanguage(pair.Metadata.PluginDirectory, Application.Current.Resources, culture);
                }
                else
                {
                    LoadPluginLanguage(pair.Metadata.PluginDirectory, null, culture);
                }

                if (pair.Plugin is IPluginI18n pluginI18n)
                {
                    pluginI18n.OnCultureInfoChanged(culture);
                }
            }
            catch { }
        }
    }

    private static void PopulateTranslationCache(string languageFile)
    {
        try
        {
            var doc = XDocument.Load(languageFile);
            var xName = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            foreach (var elem in doc.Descendants())
            {
                if (elem.Name.LocalName == "String")
                {
                    var keyAttr = elem.Attribute(xName + "Key") ?? elem.Attribute("Key");
                    if (keyAttr != null && !string.IsNullOrEmpty(keyAttr.Value))
                    {
                        TranslationCache[keyAttr.Value] = elem.Value;
                    }
                }
            }
        }
        catch { }
    }

    public static string FindLanguageFile(string languagesDir, CultureInfo culture)
    {
        if (!Directory.Exists(languagesDir)) return string.Empty;

        var exactCode = culture.Name;
        var twoLetter = culture.TwoLetterISOLanguageName;

        var candidates = Directory.GetFiles(languagesDir, "*.xaml");
        if (candidates.Length == 0) return string.Empty;

        var match = candidates.FirstOrDefault(f =>
            string.Equals(Path.GetFileNameWithoutExtension(f), exactCode, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        match = candidates.FirstOrDefault(f =>
            string.Equals(Path.GetFileNameWithoutExtension(f), twoLetter, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        if (twoLetter.Equals("zh", StringComparison.OrdinalIgnoreCase))
        {
            match = candidates.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).StartsWith("zh", StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        var en = Path.Combine(languagesDir, "en.xaml");
        if (File.Exists(en)) return en;

        return candidates.FirstOrDefault() ?? string.Empty;
    }
}
