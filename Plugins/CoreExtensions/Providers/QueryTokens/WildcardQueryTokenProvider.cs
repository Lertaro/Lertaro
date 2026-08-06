using System.IO.Enumeration;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;

public class WildcardQueryTokenProvider : IQueryTokenProvider
{
    public const string PluginId = "Lertaro.Plugins.CoreExtensions";
    public const string PrefixSettingKey = "WildcardFilterPrefix";

    public string Name => TranslationService.Get("CoreExtensions_WildcardFilterProvider_Name");

    public bool CanHandle(string token)
    {
        var prefix = GetPrefix();
        return token.Length > prefix.Length && token.StartsWith(prefix);
    }

    public Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results)
    {
        if (results == null || results.Count == 0)
            return Task.FromResult<IReadOnlyList<ISearchResult>>(Array.Empty<ISearchResult>());

        var prefix = GetPrefix();
        if (token.Length <= prefix.Length || !token.StartsWith(prefix))
            return Task.FromResult(results);

        var rawPattern = token[prefix.Length..].Trim();
        if (string.IsNullOrEmpty(rawPattern))
            return Task.FromResult(results);

        var patterns = rawPattern.Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (patterns.Length == 0)
            return Task.FromResult(results);

        var filtered = results.Where(r => MatchesAny(patterns, r.Name)).ToList();
        return Task.FromResult<IReadOnlyList<ISearchResult>>(filtered);
    }

    public string? GetHighlightText(string token) => null;

    public static bool MatchesAny(string[] patterns, string name)
    {
        foreach (var p in patterns)
        {
            var pattern = p;
            if (!pattern.StartsWith('*'))
            {
                pattern = "*" + pattern;
            }
            if (!pattern.EndsWith('*'))
            {
                pattern += "*";
            }
            if (FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true))
                return true;
        }
        return false;
    }

    private static string GetPrefix()
    {
        var prefix = PluginSettingsService.GetSetting(PluginId, PrefixSettingKey, "?");
        return string.IsNullOrEmpty(prefix) ? "?" : prefix;
    }
}
