using System.Text.RegularExpressions;

using Lertaro.Core.Indexer.NetworkDrive.Walk;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Core;

/// <summary>One priority rule that can never take effect because an exclude rule covers the same path.</summary>
public sealed record IndexRuleConflict(int PriorityIndex, int ExcludeIndex, string PriorityPattern, string ExcludePattern);

/// <summary>
/// Static, heuristic conflict detection between exclude rules and low/high/normal rules. It only reports
/// pairs it can decide from the patterns themselves; two overlapping globs or two regexes are left to the
/// engine, where exclude always wins at runtime.
/// </summary>
public static class IndexRuleConflictDetector
{
    private static readonly RegexOptions CompiledOptions =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);

    public static IReadOnlyList<IndexRuleConflict> Detect(IReadOnlyList<IndexRuleSetting> rules)
    {
        var conflicts = new List<IndexRuleConflict>();
        if (rules == null || rules.Count < 2)
            return conflicts;

        var parsed = new List<(int Index, ParsedIndexRule Rule)>();
        for (var i = 0; i < rules.Count; i++)
        {
            if (IndexRuleSyntax.TryParse(rules[i].Pattern, rules[i].Effect, out var rule, out _))
                parsed.Add((i, rule));
        }

        var matchers = new Dictionary<(IndexRuleKind Kind, string Text), object>();
        foreach (var (priorityIndex, priority) in parsed)
        {
            if (priority.Effect == IndexRuleEffect.Exclude)
                continue;

            foreach (var (excludeIndex, exclude) in parsed)
            {
                if (exclude.Effect != IndexRuleEffect.Exclude)
                    continue;

                if (Conflicts(priority, exclude, matchers))
                {
                    conflicts.Add(new IndexRuleConflict(
                        priorityIndex,
                        excludeIndex,
                        priority.Text,
                        exclude.Text));
                }
            }
        }

        return conflicts;
    }

    private static bool Conflicts(
        ParsedIndexRule priority,
        ParsedIndexRule exclude,
        Dictionary<(IndexRuleKind Kind, string Text), object> matchers)
    {
        // Same textual pattern in the same kind: no matcher needed.
        if (priority.Kind == exclude.Kind
            && priority.Pattern.Equals(exclude.Pattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (priority.Kind == IndexRuleKind.Path && exclude.Kind == IndexRuleKind.Path)
        {
            var priorityPath = ResolvePath(priority.Pattern);
            var excludePath = ResolvePath(exclude.Pattern);
            return priorityPath != null && excludePath != null
                && (IsSameOrUnder(priorityPath, excludePath) || IsSameOrUnder(excludePath, priorityPath));
        }

        // Paths are concrete, so a pattern rule on either side can be tested against them.
        if (priority.Kind == IndexRuleKind.Path)
        {
            var path = ResolvePath(priority.Pattern);
            return path != null && MatchesPattern(exclude, path, matchers);
        }

        if (exclude.Kind == IndexRuleKind.Path)
        {
            var path = ResolvePath(exclude.Pattern);
            return path != null && MatchesPattern(priority, path, matchers);
        }

        // Glob x regex and glob x glob overlap is not decidable without enumerating the filesystem.
        return false;
    }

    private static bool MatchesPattern(
        ParsedIndexRule rule,
        string path,
        Dictionary<(IndexRuleKind Kind, string Text), object> matchers)
    {
        var current = path.TrimEnd(Path.DirectorySeparatorChar);
        while (current.Length > 0)
        {
            if (MatchesLevel(rule, current, matchers))
                return true;

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent.Length >= current.Length)
                break;
            current = parent;
        }

        return false;
    }

    private static bool MatchesLevel(
        ParsedIndexRule rule,
        string full,
        Dictionary<(IndexRuleKind Kind, string Text), object> matchers)
    {
        var withSeparator = full + Path.DirectorySeparatorChar;
        var key = (rule.Kind, rule.Text);
        var name = Path.GetFileName(full);

        switch (rule.Kind)
        {
            case IndexRuleKind.Glob:
            {
                var glob = matchers.TryGetValue(key, out var cached)
                    ? (NetworkGlobPattern)cached
                    : CompileGlob(rule, key, matchers);
                return glob.IsMatch(full) || glob.IsMatch(withSeparator) || glob.IsMatch(name);
            }
            case IndexRuleKind.Regex:
            {
                var regex = matchers.TryGetValue(key, out var cached)
                    ? (Regex)cached
                    : CompileRegex(rule, key, matchers);
                return regex.IsMatch(name)
                    || regex.IsMatch(full)
                    || regex.IsMatch(withSeparator)
                    || regex.IsMatch(RelativeToDrive(full));
            }
            default:
                return false;
        }
    }

    private static NetworkGlobPattern CompileGlob(
        ParsedIndexRule rule,
        (IndexRuleKind Kind, string Text) key,
        Dictionary<(IndexRuleKind Kind, string Text), object> matchers)
    {
        var glob = GlobMatcher.Compile(UserPathResolver.Expand(rule.Pattern));
        matchers[key] = glob;
        return glob;
    }

    private static Regex CompileRegex(
        ParsedIndexRule rule,
        (IndexRuleKind Kind, string Text) key,
        Dictionary<(IndexRuleKind Kind, string Text), object> matchers)
    {
        var regex = new Regex(rule.Pattern, CompiledOptions, MatchTimeout);
        matchers[key] = regex;
        return regex;
    }

    private static string? ResolvePath(string pattern)
    {
        if (!IndexRuleSyntax.TryResolvePath(pattern, out var resolved, out _))
            return null;
        try
        {
            return Path.GetFullPath(resolved).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string RelativeToDrive(string path)
    {
        var root = Path.GetPathRoot(path);
        return root != null && path.Length > root.Length ? path[root.Length..] : path;
    }

    private static bool IsSameOrUnder(string path, string root)
    {
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase))
            return true;
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
