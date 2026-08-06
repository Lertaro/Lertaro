using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.WebSearch;

public class WebSearchInstantProvider : IInstantResultProvider
{
    public string Name => TranslationService.Get("WebSearch_ProviderName");

    public string Description => TranslationService.Get("WebSearch_ProviderDesc");

    public class SearchSourceItem
    {
        public string Name { get; set; } = string.Empty;
        public string Keyword { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string SuggestUrl { get; set; } = string.Empty;
    }

    private const int MaxSuggestions = 5;

    private const string PluginId = "Lertaro.Plugins.WebSearch";

    static WebSearchInstantProvider() =>
        // Invalidate the cached sources as soon as the host reports this plugin's settings were
        // saved, so config changes apply to the very next keystroke instead of requiring a restart.
        PluginSettingsService.SettingChanged += (pluginId, key) =>
        {
            if (string.Equals(pluginId, PluginId, StringComparison.OrdinalIgnoreCase))
            {
                _cachedSources = null;
            }
        };

    private static List<SearchSourceItem>? _cachedSources;

    private static List<SearchSourceItem> LoadSearchSources()
    {
        if (_cachedSources != null)
        {
            return _cachedSources;
        }

        try
        {
            // Unpersisted (null) falls back to this plugin's own schema DefaultValue automatically --
            // see PluginManager.GetSettingFunc -- so there's no separate hardcoded default list to
            // keep in sync here; WebSearchPlugin.GetDefaultSearchSources() is the single source of truth.
            var sources = PluginSettingsService.GetSetting<List<SearchSourceItem>>(PluginId, "SearchSources", null!);
            if (sources != null && sources.Count > 0)
            {
                var defaults = WebSearchPlugin.GetDefaultSearchSources();
                foreach (var source in sources)
                {
                    if (source.Name == "Baidu" && (string.IsNullOrWhiteSpace(source.Icon) || source.Icon.StartsWith("M15.5 14h-.79") || source.Icon.StartsWith("M9.028 20.837")))
                    {
                        source.Icon = defaults.First(d => d.Name == "Baidu").Icon;
                    }
                    else if (source.Name == "Bing" && (string.IsNullOrWhiteSpace(source.Icon) || source.Icon.StartsWith("M12 2C6.48") || source.Icon.StartsWith("M3.81 2c-.15")))
                    {
                        source.Icon = defaults.First(d => d.Name == "Bing").Icon;
                    }
                    else if (source.Name == "Wikipedia" && (string.IsNullOrWhiteSpace(source.Icon) || source.Icon.StartsWith("M12 2C6.48") || source.Icon.StartsWith("M1.5 4h3.2")))
                    {
                        source.Icon = defaults.First(d => d.Name == "Wikipedia").Icon;
                    }
                }
                _cachedSources = sources;
                return sources;
            }
        }
        catch
        {
            // Fallback
        }

        _cachedSources ??= new List<SearchSourceItem>();
        return _cachedSources;
    }

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (string.IsNullOrEmpty(query))
            yield break;

        var sources = LoadSearchSources();
        SearchSourceItem? matchedSource = null;
        var prefix = "";

        foreach (var src in sources)
        {
            var pfx = src.Keyword + " ";
            if (query.StartsWith(pfx, StringComparison.OrdinalIgnoreCase))
            {
                matchedSource = src;
                prefix = pfx;
                break;
            }
        }

        if (matchedSource == null)
            yield break;

        var searchTerm = query.Substring(prefix.Length).Trim();

        var searchEngineName = !string.IsNullOrWhiteSpace(matchedSource.Name)
            ? matchedSource.Name
            : matchedSource.Keyword.ToUpperInvariant();

        var (iconData, iconColor) = GetIconInfo(matchedSource.Icon);

        if (string.IsNullOrEmpty(searchTerm))
        {
            yield return new InstantResultItem
            {
                Title = TranslationService.Format("WebSearch_PlaceholderTitle", searchEngineName),
                Description = TranslationService.Get("WebSearch_PlaceholderDesc"),
                IconData = iconData,
                IconColor = iconColor,
                ActionType = "None"
            };
            yield break;
        }

        var searchUrl = BuildUrl(matchedSource.Url, searchTerm);

        yield return new InstantResultItem
        {
            Title = TranslationService.Format("WebSearch_ResultTitle", searchEngineName, searchTerm),
            Description = TranslationService.Get("WebSearch_ResultDesc"),
            IconData = iconData,
            IconColor = iconColor,
            ActionType = "Execute",
            ActionArgument = searchUrl
        };

        if (string.IsNullOrWhiteSpace(matchedSource.SuggestUrl))
            yield break;

        var suggestionKey = matchedSource.Keyword + ":" + searchTerm;
        var suggestions = WebSearchSuggestionFetcher.GetCached(suggestionKey);

        if (suggestions != null)
        {
            var shownCount = 0;
            foreach (var suggestion in suggestions)
            {
                if (shownCount >= MaxSuggestions)
                    break;

                if (string.Equals(suggestion, searchTerm, StringComparison.OrdinalIgnoreCase))
                    continue;

                shownCount++;
                yield return new InstantResultItem
                {
                    Title = suggestion,
                    Description = TranslationService.Format("WebSearch_SuggestionDesc", searchEngineName),
                    IconData = iconData,
                    IconColor = iconColor,
                    ActionType = "Execute",
                    ActionArgument = BuildUrl(matchedSource.Url, suggestion),
                    TabCompletion = prefix + suggestion
                };
            }
        }
        else
        {
            WebSearchSuggestionFetcher.EnsureFetchStarted(matchedSource, searchTerm, suggestionKey, prefix);
        }
    }

    internal static string BuildUrl(string template, string term)
    {
        var encoded = Uri.EscapeDataString(term);
        if (template.Contains("%s"))
        {
            return template.Replace("%s", encoded);
        }
        if (template.Contains("{0}"))
        {
            return string.Format(template, encoded);
        }
        return template + encoded;
    }

    private (string iconData, string iconColor) GetIconInfo(string iconNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(iconNameOrPath))
        {
            // Default search icon (magnifying glass)
            return ("M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z", string.Empty);
        }

        return (iconNameOrPath.Trim(), string.Empty);
    }

    public bool[]? GetHighlightMask(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return null;
        var sources = LoadSearchSources();
        SearchSourceItem? matchedSource = null;
        var prefix = "";

        foreach (var src in sources)
        {
            var pfx = src.Keyword + " ";
            if (query.StartsWith(pfx, StringComparison.OrdinalIgnoreCase))
            {
                matchedSource = src;
                prefix = pfx;
                break;
            }
        }

        if (matchedSource == null) return null;
        var mask = new bool[text.Length];
        var searchTerm = query.Substring(prefix.Length).Trim();
        if (string.IsNullOrEmpty(searchTerm)) return mask;

        return FuzzyMatchService.GetHighlightMask(text, searchTerm) ?? mask;
    }
}
