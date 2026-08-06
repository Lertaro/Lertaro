using System.IO.Enumeration;
using Lertaro.Plugins.CoreExtensions.Models;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;

public class CustomFilterQueryTokenProvider : IQueryTokenProvider
{
    public const string PluginId = "Lertaro.Plugins.CoreExtensions";
    public const string SettingKey = "CustomFilters";
    public const string PrefixSettingKey = "CustomFilterPrefix";

    public string Name => TranslationService.Get("CoreExtensions_CustomFilterProvider_Name");

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

        var rawKeywords = token[prefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawKeywords.Length == 0)
            return Task.FromResult(results);

        var configured = PluginSettingsService.GetSetting<List<CustomFilterItem>>(PluginId, SettingKey, null!);
        var filters = configured != null && configured.Count > 0 ? configured : DefaultFilters();

        var matchedRules = new List<string>();
        foreach (var kw in rawKeywords)
        {
            var match = filters.FirstOrDefault(f => f.Enabled && string.Equals(f.Keyword?.Trim(), kw, StringComparison.OrdinalIgnoreCase));
            if (match != null && !string.IsNullOrWhiteSpace(match.Rule))
            {
                matchedRules.Add(match.Rule);
            }
        }

        if (matchedRules.Count == 0)
            return Task.FromResult<IReadOnlyList<ISearchResult>>(Array.Empty<ISearchResult>());

        var combinedRule = string.Join("; ", matchedRules);
        var filtered = ApplyRule(combinedRule, results);
        return Task.FromResult(filtered);
    }

    public string? GetHighlightText(string token) => null;

    public static List<CustomFilterItem> DefaultFilters() => new()
    {
        new CustomFilterItem { Enabled = true, Keyword = "doc", Rule = "*.doc; *.docx; *.pdf; *.txt; *.ppt; *.pptx; *.xls; *.xlsx; *.csv; *.rtf; *.md; *.wps" },
        new CustomFilterItem { Enabled = true, Keyword = "img", Rule = "*.jpg; *.jpeg; *.png; *.gif; *.bmp; *.webp; *.ico; *.svg; *.tif; *.tiff; *.psd; *.ai" },
        new CustomFilterItem { Enabled = true, Keyword = "video", Rule = "*.mp4; *.mkv; *.avi; *.mov; *.wmv; *.flv; *.m4v; *.webm; *.3gp; *.rmvb; *.ts" },
        new CustomFilterItem { Enabled = true, Keyword = "audio", Rule = "*.mp3; *.wav; *.flac; *.aac; *.ogg; *.m4a; *.wma; *.ape" },
        new CustomFilterItem { Enabled = true, Keyword = "zip", Rule = "*.zip; *.rar; *.7z; *.tar; *.gz; *.bz2; *.xz; *.iso" }
    };

    public static List<object> DefaultFiltersSchema() => new()
    {
        new Dictionary<string, object> { ["Enabled"] = true, ["Keyword"] = "doc", ["Rule"] = "*.doc; *.docx; *.pdf; *.txt; *.ppt; *.pptx; *.xls; *.xlsx; *.csv; *.rtf; *.md; *.wps" },
        new Dictionary<string, object> { ["Enabled"] = true, ["Keyword"] = "img", ["Rule"] = "*.jpg; *.jpeg; *.png; *.gif; *.bmp; *.webp; *.ico; *.svg; *.tif; *.tiff; *.psd; *.ai" },
        new Dictionary<string, object> { ["Enabled"] = true, ["Keyword"] = "video", ["Rule"] = "*.mp4; *.mkv; *.avi; *.mov; *.wmv; *.flv; *.m4v; *.webm; *.3gp; *.rmvb; *.ts" },
        new Dictionary<string, object> { ["Enabled"] = true, ["Keyword"] = "audio", ["Rule"] = "*.mp3; *.wav; *.flac; *.aac; *.ogg; *.m4a; *.wma; *.ape" },
        new Dictionary<string, object> { ["Enabled"] = true, ["Keyword"] = "zip", ["Rule"] = "*.zip; *.rar; *.7z; *.tar; *.gz; *.bz2; *.xz; *.iso" }
    };

    public static IReadOnlyList<ISearchResult> ApplyRule(string rule, IReadOnlyList<ISearchResult> results)
    {
        var rawTokens = rule.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawTokens.Length == 0)
            return results;

        var subRules = new List<Func<ISearchResult, bool>>();
        foreach (var t in rawTokens)
        {
            var lower = t.ToLowerInvariant();
            if (lower == ":f" || lower == "folder" || lower == "dir")
            {
                subRules.Add(r => r.IsDir);
            }
            else if (lower == ":-f" || lower == "file")
            {
                subRules.Add(r => !r.IsDir);
            }
            else
            {
                var pattern = lower;
                if (!pattern.Contains('*') && !pattern.Contains('?'))
                {
                    var cleanExt = pattern.TrimStart('.');
                    pattern = $"*.{cleanExt}";
                }
                subRules.Add(r => FileSystemName.MatchesSimpleExpression(pattern, r.Name, ignoreCase: true));
            }
        }

        if (subRules.Count == 0)
            return results;

        return results.Where(r => subRules.Any(ruleFunc => ruleFunc(r))).ToList();
    }

    private static string GetPrefix()
    {
        var prefix = PluginSettingsService.GetSetting(PluginId, PrefixSettingKey, "@");
        return string.IsNullOrEmpty(prefix) ? "@" : prefix;
    }
}
