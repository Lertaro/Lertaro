using System.IO.Enumeration;
using Lertaro.Plugins.CoreExtensions.Models;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;

public class CustomFilterQueryTokenProvider : IQueryTokenProvider
{
    public const string PluginId = "Lertaro.Plugins.CoreExtensions";
    public const string SettingKey = "CustomFilters";
    public const string PrefixSettingKey = "CustomFilterPrefix";

    /// <summary>
    /// The storage key of the list this one replaced. It is no longer a settings field: its rows are folded
    /// into <see cref="SettingKey"/> on first read (see MergeSidebarRowsIntoFiltersOnce) and then removed.
    /// </summary>
    public const string LegacySidebarSettingKey = "SidebarCustomFilters";

    public string Name => TranslationService.Get("CoreExtensions_CustomFilterProvider_Name");

    public bool CanHandle(string token)
    {
        var prefix = GetConfiguredPrefix();
        return token.Length > prefix.Length && token.StartsWith(prefix);
    }

    public Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results)
    {
        if (results == null || results.Count == 0)
            return Task.FromResult<IReadOnlyList<ISearchResult>>(Array.Empty<ISearchResult>());

        var prefix = GetConfiguredPrefix();
        if (token.Length <= prefix.Length || !token.StartsWith(prefix))
            return Task.FromResult(results);

        var rawKeywords = token[prefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawKeywords.Length == 0)
            return Task.FromResult(results);

        var filters = GetConfiguredFilters();

        var matchedRules = new List<string>();
        foreach (var kw in rawKeywords)
        {
            var match = filters.FirstOrDefault(f => f.Enabled && string.Equals(f.Keyword?.Trim(), kw, StringComparison.OrdinalIgnoreCase));
            if (match != null && !string.IsNullOrWhiteSpace(match.Rule))
            {
                var expandedRule = CustomFilterRuleResolver.Expand(match.Rule, filters, prefix);
                if (!string.IsNullOrWhiteSpace(expandedRule))
                    matchedRules.Add(expandedRule);
            }
        }

        if (matchedRules.Count == 0)
            return Task.FromResult<IReadOnlyList<ISearchResult>>(Array.Empty<ISearchResult>());

        var combinedRule = string.Join("; ", matchedRules);
        var filtered = ApplyRule(combinedRule, results, filters, prefix);
        return Task.FromResult(filtered);
    }

    public string? GetHighlightText(string token) => null;

    /// <summary>
    /// The built-in 目录分类 rows. The rule text is the one category table's own glob form, so a keyword here
    /// and the quick window's filter tab cannot answer differently for the same file; the keywords themselves
    /// are what the user types, so they stay here.
    /// </summary>
    private static List<CustomFilterItem> BuiltInFilters() =>
    [
        Row("doc", SearchCategoryFilter.Document),
        Row("img", SearchCategoryFilter.Image),
        Row("video", SearchCategoryFilter.Video),
        Row("audio", SearchCategoryFilter.Audio),
        Row("zip", SearchCategoryFilter.Archive),
        // "app" was never one of the built-in keywords, so a user wanting "only programs" had to write their
        // own rule; the quick window's 应用 tab already answered that question.
        Row("app", SearchCategoryFilter.Application),
    ];

    private static CustomFilterItem Row(string keyword, SearchCategoryFilter category) => new()
    {
        Enabled = true,
        Keyword = keyword,
        Rule = SearchCategoryClassifier.RuleOf(category),
    };

    public static List<CustomFilterItem> DefaultFilters() => BuiltInFilters();

    // The same rows as a settings schema, built from the one list rather than written out a second time:
    // these two used to be two hand-maintained copies of the same five rules, and a rule edited in only one of
    // them silently changed what the settings page shows as its default.
    public static List<object> DefaultFiltersSchema() =>
        BuiltInFilters().ConvertAll(filter => (object)new Dictionary<string, object>
        {
            ["Enabled"] = filter.Enabled,
            ["Keyword"] = filter.Keyword,
            ["Label"] = filter.Label,
            ["Rule"] = filter.Rule,
            ["ShowAsRow"] = filter.ShowAsRow,
            ["Icon"] = filter.Icon,
        });

    public static IReadOnlyList<ISearchResult> ApplyRule(string rule, IReadOnlyList<ISearchResult> results) => ApplyRule(rule, results, GetConfiguredFilters(), GetConfiguredPrefix());

    public static Func<ISearchResult, bool> BuildPredicate(
        string rule,
        IReadOnlyList<CustomFilterItem> filters,
        string? prefix = null,
        bool allowDisabledReferences = false)
    {
        var expandedRule = ExpandRule(rule, filters, prefix, allowDisabledReferences);
        var rawTokens = expandedRule.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawTokens.Length == 0)
            return _ => false;

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

        return result => subRules.Any(ruleFunc => ruleFunc(result));
    }

    public static IReadOnlyList<ISearchResult> ApplyRule(
        string rule,
        IReadOnlyList<ISearchResult> results,
        IReadOnlyList<CustomFilterItem> filters,
        string? prefix = null,
        bool allowDisabledReferences = false)
    {
        if (string.IsNullOrWhiteSpace(ExpandRule(rule, filters, prefix, allowDisabledReferences)))
            return results;

        var predicate = BuildPredicate(rule, filters, prefix, allowDisabledReferences);
        return results.Where(predicate).ToList();
    }

    /// <summary>
    /// The one category list: typed keywords and, for the rows flagged <see cref="CustomFilterItem.ShowAsRow"/>,
    /// the sidebar and filter-bar rows. Reading it is also where the old separate sidebar list is folded in --
    /// see <see cref="MergeSidebarRowsIntoFiltersOnce"/>.
    /// </summary>
    public static List<CustomFilterItem> GetConfiguredFilters()
    {
        MergeSidebarRowsIntoFiltersOnce();
        var configured = PluginSettingsService.GetSetting<List<CustomFilterItem>>(PluginId, SettingKey, null!);
        return configured != null && configured.Count > 0 ? configured : DefaultFilters();
    }

    /// <summary>The rows the host should show: a category only becomes one when it is asked to.</summary>
    public static List<CustomFilterItem> GetConfiguredRows() => GetConfiguredFilters().Where(f => f.ShowAsRow).ToList();

    /// <summary>
    /// Folds the legacy <c>SidebarCustomFilters</c> list into this one and removes it, once per process.
    /// </summary>
    /// <remarks>
    /// The two lists had the same shape and the same rule language, and the split cost three things: a
    /// category could be defined twice with two different rules (and the row silently won for the click while
    /// the keyword silently won for the typing), a row's trigger keyword doubled as its untranslated display
    /// name, and adding a category meant knowing which of the two lists "row" and "keyword" lived in.
    ///
    /// A row whose keyword already names a category switches that category on rather than being appended, so
    /// the usual case -- a row that was only ever a mirror of <c>@doc</c> -- merges into one entry instead of
    /// two. Any other row is appended whole, keeping its own rule, its icon, and its keyword as the display
    /// name it was already showing.
    ///
    /// The read path writes, which is what <c>PluginManager</c> already does to normalize a stored value: this
    /// is the only accessor both consumers call, so it is the only place the merge can be guaranteed to have
    /// happened before either reads. The legacy key is gone after the first pass, so the second pass is one
    /// dictionary lookup.
    /// </remarks>
    internal static void MergeSidebarRowsIntoFiltersOnce()
    {
        // No latch: the whole check is one dictionary miss once the legacy key is gone, and a latch would make
        // the merge depend on which caller happened to read the list first in this process.
        {
            try
            {
                var legacyRows = PluginSettingsService.GetSetting<List<CustomFilterItem>>(
                    PluginId, LegacySidebarSettingKey, null!);
                if (legacyRows == null || legacyRows.Count == 0)
                    return;

                var filters = PluginSettingsService.GetSetting<List<CustomFilterItem>>(PluginId, SettingKey, null!);
                if (filters == null || filters.Count == 0)
                    filters = DefaultFilters();

                foreach (var row in legacyRows)
                {
                    var keyword = row.Keyword?.Trim() ?? string.Empty;
                    if (keyword.Length == 0)
                        continue;

                    var existing = filters.FirstOrDefault(f =>
                        string.Equals(f.Keyword?.Trim(), keyword, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.ShowAsRow = true;
                        if (string.IsNullOrWhiteSpace(existing.Icon) && !string.IsNullOrWhiteSpace(row.Icon))
                            existing.Icon = row.Icon;
                        continue;
                    }

                    filters.Add(new CustomFilterItem
                    {
                        Enabled = row.Enabled,
                        Keyword = keyword,
                        Label = keyword,
                        Icon = row.Icon,
                        Rule = row.Rule,
                        ShowAsRow = true,
                    });
                }

                PluginSettingsService.SetSetting(PluginId, SettingKey, filters);
                PluginSettingsService.SetSetting(PluginId, LegacySidebarSettingKey, null);
                PluginSettingsService.NotifySettingChanged(PluginId, SettingKey);
            }
            catch (Exception ex)
            {
                Logger.Log($"[CustomFilterQueryTokenProvider] Sidebar filter merge skipped: {ex.Message}", LogLevel.Warn);
            }
        }
    }

    public static string ExpandRule(
        string rule,
        IReadOnlyList<CustomFilterItem> filters,
        string? prefix = null,
        bool allowDisabledReferences = false) => CustomFilterRuleResolver.Expand(rule, filters, prefix ?? GetConfiguredPrefix(), allowDisabledReferences);

    public static string GetConfiguredPrefix()
    {
        var prefix = PluginSettingsService.GetSetting(PluginId, PrefixSettingKey, "@");
        return string.IsNullOrEmpty(prefix) ? "@" : prefix;
    }
}
