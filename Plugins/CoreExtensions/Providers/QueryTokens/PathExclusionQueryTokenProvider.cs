using System.IO;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;

// Built-in reference implementation of the "<keyword> ::<fzf-expr>" query suffix token (e.g.
// "<keyword> :xyz,:lgbb" alongside other tokens) -- keeps only results with a path segment (any
// ancestor folder name, or the file's own name) that matches the fzf expression, using the host's own
// fuzzy-match engine (FuzzyMatchService) so this behaves identically to a real search match, alias
// fallback included, instead of a hand-rolled substring check.
public class PathExclusionQueryTokenProvider : IQueryTokenProvider
{
    public const string PluginId = "Lertaro.Plugins.CoreExtensions";
    public const string SettingKey = "PathExclusionPrefix";

    public string Name => TranslationService.Get("CoreExtensions_PathExclusionProvider_Name");

    public bool CanHandle(string token)
    {
        var prefix = GetPrefix();
        return token.Length > prefix.Length && token.StartsWith(prefix);
    }

    public Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results)
    {
        var prefix = GetPrefix();
        var pattern = token.Length > prefix.Length && token.StartsWith(prefix) ? token[prefix.Length..] : token;
        if (string.IsNullOrWhiteSpace(pattern))
            return Task.FromResult(results);

        var filtered = results.Where(r => AnySegmentMatches(r.FullPath, pattern)).ToList();
        return Task.FromResult<IReadOnlyList<ISearchResult>>(filtered);
    }

    public string? GetHighlightText(string token)
    {
        var prefix = GetPrefix();
        return token.Length > prefix.Length && token.StartsWith(prefix) ? token[prefix.Length..] : null;
    }

    private static string GetPrefix()
    {
        var prefix = PluginSettingsService.GetSetting(PluginId, SettingKey, ":");
        return string.IsNullOrEmpty(prefix) ? ":" : prefix;
    }

    // Splitting the full path covers every ancestor folder AND the file's own leaf name in one pass --
    // "keep if any path component matches" needs no special-casing between the two.
    private static bool AnySegmentMatches(string fullPath, string pattern)
    {
        foreach (var segment in fullPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (FuzzyMatchService.IsMatch(pattern, segment))
                return true;
        }
        return false;
    }
}
