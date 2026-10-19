namespace Lertaro.Core;

/// <summary>
/// Converts the three legacy exclusion lists into <see cref="UserSettings.IndexRules"/>, and keeps the
/// legacy lists populated on save so an older build can still read the exclusions it understands.
/// </summary>
public static class IndexRuleMigration
{
    /// <summary>
    /// Returns the settings' rules, migrating once when they are absent. Migration is in-memory only:
    /// startup never writes settings, so a read-only install directory cannot break the app; the next
    /// normal save persists the result.
    /// </summary>
    public static List<IndexRuleSetting> Ensure(UserSettings settings)
    {
        if (settings.IndexRules != null)
            return settings.IndexRules;

        settings.IndexRules = IsLegacyDefault(settings)
            ? IndexRuleDefaults.Create()
            : BuildFromCustomLegacy(settings);
        return settings.IndexRules;
    }

    /// <summary>Strips the delimiters that <see cref="IndexRuleSyntax"/> requires but the old regex list never stored.</summary>
    public static string WrapLegacyRegex(string body) => "/" + body + "/";

    public static void CollectExcludeLists(
        IReadOnlyList<IndexRuleSetting> rules,
        List<string> paths,
        List<string> globs,
        List<string> regexes)
    {
        foreach (var rule in rules)
        {
            if (rule.Effect != IndexRuleEffect.Exclude)
                continue;
            if (!IndexRuleSyntax.TryParse(rule.Pattern, rule.Effect, out var parsed, out _))
                continue;

            var target = parsed.Kind switch
            {
                IndexRuleKind.Path => paths,
                IndexRuleKind.Glob => globs,
                _ => regexes,
            };
            var value = parsed.Kind == IndexRuleKind.Path ? parsed.Text : parsed.Pattern;
            if (!string.IsNullOrWhiteSpace(value) && !target.Contains(value, StringComparer.OrdinalIgnoreCase))
                target.Add(value);
        }
    }

    /// <summary>
    /// Rewrites the legacy lists from the rules' exclusion subset. Called on save, so downgrading keeps
    /// the excluding behaviour even though an older build cannot see the low/high rules.
    /// </summary>
    public static void MirrorLegacy(UserSettings settings)
    {
        if (settings.IndexRules == null)
            return;

        var paths = new List<string>();
        var globs = new List<string>();
        var regexes = new List<string>();
        CollectExcludeLists(settings.IndexRules, paths, globs, regexes);

        settings.ExcludedPaths = paths;
        settings.IgnoredPathGlobs = globs;
        settings.IgnoredPathRegexes = regexes;
    }

    private static List<IndexRuleSetting> BuildFromCustomLegacy(UserSettings settings)
    {
        var rules = new List<IndexRuleSetting>();
        foreach (var path in settings.ExcludedPaths)
            AddLegacy(rules, path, IndexRuleKind.Path);
        foreach (var glob in settings.IgnoredPathGlobs)
            AddLegacy(rules, glob, IndexRuleKind.Glob);
        foreach (var regex in settings.IgnoredPathRegexes)
            AddLegacy(rules, regex, IndexRuleKind.Regex);

        // A customised configuration keeps everything it had as an exclusion, then gains the demotions
        // the new defaults introduce. Overlaps are intentional: exclusion wins at runtime, and the
        // conflict banner is how the user finds and resolves them.
        rules.AddRange(IndexRuleDefaults.CreateLow());
        return rules;
    }

    private static void AddLegacy(List<IndexRuleSetting> rules, string? pattern, IndexRuleKind kind)
    {
        var text = IndexRuleSyntax.Clean(pattern);
        if (text.Length == 0)
            return;

        if (kind == IndexRuleKind.Regex)
            text = WrapLegacyRegex(text);

        rules.Add(new IndexRuleSetting { Pattern = text, Effect = IndexRuleEffect.Exclude });
    }

    private static bool IsLegacyDefault(UserSettings settings)
    {
        return SameSet(settings.ExcludedPaths, IndexRuleDefaults.LegacyExcludedPaths)
               && SameSet(settings.IgnoredPathGlobs, IndexRuleDefaults.LegacyIgnoredGlobs)
               && settings.IgnoredPathRegexes.Count == 0;
    }

    private static bool SameSet(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        if (actual.Count != expected.Count)
            return false;

        var left = actual.Select(NormalizeLegacy).Where(value => value.Length > 0)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        var right = expected.Select(NormalizeLegacy)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        return left.Length == right.Length && left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeLegacy(string? value)
    {
        var text = IndexRuleSyntax.Clean(value).Replace('/', '\\').TrimEnd('\\');
        return text;
    }
}
