using System.IO;
using System.Resources;
using System.Windows;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.Theming;

public class CoreThemeProvider : IThemeProvider
{
    public string Name => "Core Theme Provider";

    public IEnumerable<ITheme> GetThemes()
    {
        var themes = new List<ITheme>();
        var assembly = typeof(CoreThemeProvider).Assembly;
        var assemblyName = assembly.GetName().Name;
        var resourceName = $"{assemblyName}.g.resources";

        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new ResourceReader(stream);
                foreach (System.Collections.DictionaryEntry entry in reader)
                {
                    var key = entry.Key?.ToString() ?? string.Empty;
                    // WPF compiled resources use lowercase relative paths ending in .baml
                    if (key.StartsWith("resources/themes/", StringComparison.OrdinalIgnoreCase) &&
                        key.EndsWith(".baml", StringComparison.OrdinalIgnoreCase))
                    {
                        var relativeXamlPath = key.Substring(0, key.Length - 5) + ".xaml";
                        var packUri = $"pack://application:,,,/{assemblyName};component/{relativeXamlPath}";

                        try
                        {
                            var dict = new ResourceDictionary
                            {
                                Source = new Uri(packUri, UriKind.Absolute)
                            };

                            // Extract metadata defined inside the ResourceDictionary
                            var id = dict.Contains("ThemeId") ? (dict["ThemeId"]?.ToString() ?? string.Empty) : Path.GetFileNameWithoutExtension(relativeXamlPath);
                            var isDark = dict.Contains("IsDark") && dict["IsDark"] is bool darkVal && darkVal;

                            if (!string.IsNullOrEmpty(id))
                            {
                                themes.Add(new CoreTheme(id, isDark, packUri));
                            }
                        }
                        catch
                        {
                            // Ignore or skip corrupted resource files
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback to empty if assembly resources cannot be read
        }

        // Order themes: Light first, Dark second, others alphabetically
        return themes.OrderBy(t => t.Id == "Light" ? 0 : t.Id == "Dark" ? 1 : 2).ThenBy(t => t.Id);
    }
}

public class CoreTheme : ITheme
{
    private readonly string _packUri;
    private ResourceDictionary? _cachedResources;

    public string Id { get; }
    public string DisplayName => TranslationService.Get($"Theme_{Id}");
    public bool IsDark { get; }

    public CoreTheme(string id, bool isDark, string packUri)
    {
        Id = id;
        IsDark = isDark;
        _packUri = packUri;
    }

    public ResourceDictionary GetResources() => _cachedResources ??= new ResourceDictionary
    {
        Source = new Uri(_packUri, UriKind.Absolute)
    };

    public double WindowOpacity => (double)GetResources()["WindowOpacity"];
}
