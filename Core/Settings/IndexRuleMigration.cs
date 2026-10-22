namespace Lertaro.Core;

/// <summary>
/// Converts the three legacy exclusion lists into <see cref="UserSettings.IndexRules"/>, and keeps the
/// legacy lists populated on save so an older build can still read the exclusions it understands.
/// </summary>
public static class IndexRuleMigration
{
    /// <summary>
    /// Returns the settings' rules, migrating once when they are absent and retiring a rule the shipped
    /// defaults no longer carry when the saved list is still exactly the previous stock one. Migration is
    /// in-memory only: startup never writes settings, so a read-only install directory cannot break the app;
    /// the next normal save persists the result.
    /// </summary>
    public static List<IndexRuleSetting> Ensure(UserSettings settings)
    {
        if (settings.IndexRules != null)
        {
            RetirePreviousStockDefault(settings.IndexRules);
            return settings.IndexRules;
        }

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

    // The shipped list before IndexRuleDefaults.RetiredExtensionlessRegex was dropped, as stable keys.
    private static readonly string[] PreviousStockKeys = IndexRuleDefaults.Create()
        .Append(new IndexRuleSetting
        {
            Pattern = IndexRuleDefaults.RetiredExtensionlessRegex,
            Effect = IndexRuleEffect.Low,
        })
        .Select(KeyOf)
        .OrderBy(key => key, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Removes <see cref="IndexRuleDefaults.RetiredExtensionlessRegex"/> from a settings file that still
    /// holds the exact previous stock list. A list the user touched is left alone -- the rule is theirs at
    /// that point, and deleting something someone typed on purpose is worse than leaving a demotion they can
    /// see and delete themselves. In memory, like the rest of the migration: the next save persists it.
    /// </summary>
    private static void RetirePreviousStockDefault(List<IndexRuleSetting> rules)
    {
        if (rules.Count != PreviousStockKeys.Length)
            return;

        var keys = rules.Select(KeyOf).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        if (!keys.SequenceEqual(PreviousStockKeys, StringComparer.Ordinal))
            return;

        rules.RemoveAll(rule => rule.Pattern == IndexRuleDefaults.RetiredExtensionlessRegex
                                && rule.Effect == IndexRuleEffect.Low);
    }

    // The effect is part of the identity: the same pattern may legitimately appear as an exclusion and as a
    // demotion, and a conflict is reported rather than rewritten.
    private static string KeyOf(IndexRuleSetting rule) => $"{(int)rule.Effect}|{rule.Pattern}";

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
