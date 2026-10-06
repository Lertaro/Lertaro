using System.Text.RegularExpressions;
using Lertaro.Plugins.CoreExtensions.Models;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;

// Built-in implementation of the "\<category>" query token, e.g. "report \audio".
//
// Each category keyword is resolved to the regex its configured rule denotes -- "\audio" becomes
// "\.(?:ogg|m4a|mp3|wav|flac|aac)$" -- and matched against the result's file name. The rule field keeps
// its historical "*.ext; *.ext2" spelling; it is translated to a regex internally (see RuleToRegex), so
// existing user settings keep working unchanged. Keyword references between rules (\audio inside another
// rule's text) are expanded first, exactly as the sidebar path does -- the field's own description tells
// the user a keyword is "the name other rules use to reference it".
public class CustomFilterQueryTokenProvider : IQueryTokenProvider
{
    public const string PluginId = "Lertaro.Plugins.CoreExtensions";
    public const string SettingKey = "CustomFilters";

    // The key this plugin used to store its prefix under, kept only so an upgrade can find and clear the
    // stale value (see LegacySettingsAdvisor). Nothing reads it as a setting any more: the prefix comes
    // from the search syntax itself, through SearchSyntaxService.
    public const string LegacyPrefixSettingKey = "CustomFilterPrefix";

    // CultureInvariant is not decoration: IgnoreCase alone folds through CurrentCulture, and the app only
    // sets CurrentUICulture (see TranslationManager), so on a tr-TR machine 'I' folds to 'ı' and "\.gif$"
    // stops matching PHOTO.GIF. The compiled result then sits in RegexCache carrying the wrong folding for
    // the rest of the process. This path replaced FileSystemName.MatchesSimpleExpression, which was
    // ordinal, so losing it here is a behaviour change rather than a pre-existing quirk -- and four other
    // regexes in this project already pass the flag.
    private static readonly RegexOptions MatchOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // Compiled regexes are cached per translated pattern. A filter runs on every keystroke against the
    // whole fetched result set, so recompiling here would be the single most expensive thing in the
    // token chain. The cache is bounded and cleared at the cap, so repeated rule edits cannot grow it
    // without limit.
    private const int MaxRegexCacheEntries = 256;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> RegexCache = new(StringComparer.Ordinal);
    private static readonly object RegexCacheGate = new();

    // Test seam: the bound is only observable from the outside through the count.
    internal static int CachedRegexCount => RegexCache.Count;

    /// <summary>
    /// The storage key of the list this one replaced. It is no longer a settings field: its rows are folded
    /// into <see cref="SettingKey"/> on first read (see MergeSidebarRowsIntoFiltersOnce) and then removed.
    /// </summary>
    public const string LegacySidebarSettingKey = "SidebarCustomFilters";

    public string Name => TranslationService.Get("CoreExtensions_CustomFilterProvider_Name");

    public bool CanHandle(string token)
    {
        var prefix = GetConfiguredPrefix();
        return token.Length > prefix.Length && token.StartsWith(prefix, StringComparison.Ordinal);
    }

    public Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results)
        => ApplyAsync(token, results, CancellationToken.None);

    public Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (results == null || results.Count == 0)
            return Task.FromResult<IReadOnlyList<ISearchResult>>(Array.Empty<ISearchResult>());

        var prefix = GetConfiguredPrefix();
        if (token.Length <= prefix.Length || !token.StartsWith(prefix, StringComparison.Ordinal))
            return Task.FromResult(results);

        var keyword = token[prefix.Length..].Trim();
        if (keyword.Length == 0)
            return Task.FromResult(results);

        // Longest-match first: the trigger key may be alphanumeric, so a user who configured both "a"
        // and "audio" must still get the "audio" rule for "\audio" rather than the "a" rule.
        var filter = ResolveLongestMatch(keyword);
        if (filter == null || string.IsNullOrWhiteSpace(filter.Rule))
            return Task.FromResult<IReadOnlyList<ISearchResult>>(Array.Empty<ISearchResult>());

        // A rule's keyword references have to be expanded BEFORE it is translated: "the name other rules
        // use to reference it" is what the field's own description promises the user, and a bare
        // RuleToRegex turns "\audio" into the literal name pattern "\.\audio$" -- which no Windows file
        // name can contain, so the reference silently contributed nothing, and a rule made only of
        // references emptied the result set with no explanation on screen. The sidebar path (see
        // BuildPredicate) has always expanded; this path had dropped it.
        var (nameRegex, admitDirs, admitAnyFile) = TranslateRule(ExpandRule(filter.Rule, GetConfiguredFilters(), prefix));
        if (nameRegex == null && !admitDirs && !admitAnyFile)
            // Nothing in the rule can select a row -- an empty rule, or one whose every token was a
            // reference to another filter that resolved to nothing.
            return Task.FromResult<IReadOnlyList<ISearchResult>>(Array.Empty<ISearchResult>());

        var filtered = new List<ISearchResult>();
        foreach (var result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (result.IsDir ? admitDirs : admitAnyFile || (nameRegex?.IsMatch(result.Name) ?? false))
                    filtered.Add(result);
            }
            catch (RegexMatchTimeoutException)
            {
                // A pathological rule/name pair is a miss; the next row still observes cancellation.
            }
        }
        return Task.FromResult<IReadOnlyList<ISearchResult>>(filtered);
    }

    // Splits an already-expanded rule into the one name regex it implies plus the two things a name regex
    // cannot express. "folder", "dir" and ":f" are statements about directory-ness, and "file"/":-f" about
    // the absence of it -- BuildPredicate reads them that way, so translating them into name alternatives
    // instead made the two paths answer differently for one rule text, and left the token path unable to
    // return a folder at all. Name tokens deliberately stay files-only here (unlike the sidebar, which
    // matches a name against rows of either kind): a category filter such as "\img" should not surface a
    // DIRECTORY called photos.jpg.
    private static (Regex? NameRegex, bool AdmitDirs, bool AdmitAnyFile) TranslateRule(string expandedRule)
    {
        List<string>? nameTokens = null;
        var admitDirs = false;
        var admitAnyFile = false;
        foreach (var raw in expandedRule.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "folder":
                case "dir":
                case ":f":
                    admitDirs = true;
                    break;
                case "file":
                case ":-f":
                    admitAnyFile = true;
                    break;
                default:
                    (nameTokens ??= []).Add(raw);
                    break;
            }
        }

        // Rejoined with the separator RuleToRegex splits on, so the cache key stays one per distinct rule.
        return (nameTokens is { Count: > 0 } ? GetRegex(string.Join("; ", nameTokens)) : null, admitDirs, admitAnyFile);
    }

    public string? GetHighlightText(string token) => null;

    // The configured keyword whose text the typed one extends, preferring the longest -- "\audio" picks
    // "audio" over "a" when both exist.
    private static CustomFilterItem? ResolveLongestMatch(string keyword)
    {
        CustomFilterItem? best = null;
        foreach (var filter in GetConfiguredFilters())
        {
            var name = filter.Keyword?.Trim();
            if (!filter.Enabled || string.IsNullOrEmpty(name))
                continue;
            if (!keyword.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (best == null || name.Length > best.Keyword!.Trim().Length)
                best = filter;
        }

        return best;
    }

    // The fast path is a plain read, so a rule that is already compiled never takes the lock. The cap is
    // enforced inside it, where the count cannot change underneath the check. Clearing rather than
    // evicting one entry keeps the rules a user actually configured (a handful) resident, and costs at
    // most one recompile per MaxRegexCacheEntries new rules.
    private static Regex? GetRegex(string rule)
    {
        if (RegexCache.TryGetValue(rule, out var cached))
            return cached;

        lock (RegexCacheGate)
        {
            if (RegexCache.TryGetValue(rule, out cached))
                return cached;

            if (RegexCache.Count >= MaxRegexCacheEntries)
                RegexCache.Clear();

            Regex compiled;
            try
            {
                var pattern = RuleToRegex(rule);
                try
                {
                    compiled = new Regex(pattern, MatchOptions | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(250));
                }
                catch (NotSupportedException)
                {
                    // Large wildcard alternatives can exceed the non-backtracking engine's state limit.
                    compiled = new Regex(pattern, MatchOptions, TimeSpan.FromMilliseconds(250));
                }
            }
            catch (ArgumentException)
            {
                // A user-authored rule that doesn't translate to a valid pattern matches nothing rather
                // than failing the whole search.
                compiled = new Regex("(?!)", MatchOptions);
            }

            RegexCache[rule] = compiled;
            return compiled;
        }
    }

    // "*.doc; *.docx; *.pdf" and "audio" (a bare word is read as an extension) both become one
    // alternation anchored at the end of the name. Anything that already carries wildcard syntax keeps
    // being treated as a wildcard pattern rather than a regex, so rules written before the rewrite
    // behave as they did.
    //
    // Only NAME tokens reach here -- TranslateRule has already taken the directory-ness words out, since
    // an alternative like "(?:folder|dir)" against a file name matches directory.txt and no folder at all.
    internal static string RuleToRegex(string rule)
    {
        var alternatives = new List<string>();
        foreach (var raw in rule.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = raw.ToLowerInvariant();
            if (token.Contains('*') || token.Contains('?'))
            {
                alternatives.Add(WildcardToRegex(token));
                continue;
            }

            alternatives.Add(@"\." + Regex.Escape(token.TrimStart('.')) + "$");
        }

        return alternatives.Count == 0 ? "(?!)" : "(?:" + string.Join("|", alternatives) + ")";
    }

    private static string WildcardToRegex(string wildcard)
    {
        var builder = new System.Text.StringBuilder("^");
        foreach (var c in wildcard)
        {
            builder.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString())
            });
        }

        return builder.Append('$').ToString();
    }

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

    public static IReadOnlyList<ISearchResult> ApplyRule(string rule, IReadOnlyList<ISearchResult> results)
        => ApplyRule(rule, results, GetConfiguredFilters(), GetConfiguredPrefix());

    // Kept for the sidebar filter providers, which bind a filename pattern list to a filter definition.
    // Still wildcard-based: those patterns come from the same "*.ext" rule strings, and the sidebar has
    // no reason to run a regex per row when a simple-expression match does the job.
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
                subRules.Add(r => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, r.Name, ignoreCase: true));
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

    /// <summary>
    /// The character this provider's tokens start with: the host's own, since the host's scanner is what
    /// decides which words become tokens and it hands them over with that character still attached.
    /// </summary>
    /// <remarks>
    /// Kept as a method with this name because the sidebar filters and the rule resolver share it, but it is
    /// no longer a setting of this plugin. It used to be one ("CustomFilterPrefix"), and a value that
    /// disagreed with the host's meant every token this provider owned was lifted out of the query and then
    /// claimed by nobody -- an empty result list with nothing on screen to explain it. There is no second
    /// value to keep in step now.
    /// </remarks>
    public static string GetConfiguredPrefix() => SearchSyntaxService.TokenPrefix.ToString();
}
