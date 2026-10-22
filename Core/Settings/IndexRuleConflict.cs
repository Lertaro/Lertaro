using System.Text.RegularExpressions;

using Lertaro.Core.Indexer.NetworkDrive.Walk;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Core;

/// <summary>One priority rule that can never take effect because an exclude rule covers the same path.</summary>
public sealed record IndexRuleConflict(int PriorityIndex, int ExcludeIndex, string PriorityPattern, string ExcludePattern);

/// <summary>
/// Static, heuristic conflict detection between exclude rules and low/high/normal rules. It reports a pair
/// only when the exclude rule covers the whole range of the priority rule, and abstains when coverage cannot
/// be proven: overlapping globs, a regex priority rule against a path exclude, and a priority rule that
/// reaches outside the excluded subtree are all left to the engine, where exclude wins at runtime anyway.
/// Missing a real conflict is cheaper than flagging a rule that still takes effect.
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
            // One-way: an exclude above the priority path removes it, an exclude below only takes a part.
            return priorityPath != null && excludePath != null && IsSameOrUnder(priorityPath, excludePath);
        }

        // A concrete priority path can be tested against any exclude matcher.
        if (priority.Kind == IndexRuleKind.Path)
        {
            var path = ResolvePath(priority.Pattern);
            return path != null && MatchesPattern(exclude, path, matchers);
        }

        if (exclude.Kind == IndexRuleKind.Path)
        {
            var excludePath = ResolvePath(exclude.Pattern);
            return excludePath != null && PatternIsCoveredByPath(priority, excludePath);
        }

        // Glob x regex and glob x glob overlap is not decidable without enumerating the filesystem.
        return false;
    }

    /// <summary>
    /// True when every path a glob rule matches is known to live inside <paramref name="excludePath"/>. The
    /// literal text before the first wildcard bounds the matches: when it ends on a separator that directory
    /// is the deepest all matches share, otherwise the wildcard sits inside a segment and only its parent
    /// directory is shared. Relative globs and regexes have no such anchor and are never proven.
    /// </summary>
    private static bool PatternIsCoveredByPath(ParsedIndexRule priority, string excludePath)
    {
        if (priority.Kind != IndexRuleKind.Glob)
            return false;

        var text = UserPathResolver.Expand(priority.Pattern);
        if (!Path.IsPathFullyQualified(text) || IsDevicePath(text))
            return false;

        // A pattern with ".." no longer matches the subtree its literal prefix names.
        if (text.Split(Path.DirectorySeparatorChar).Contains(".."))
            return false;

        var wildcard = IndexOfGlobWildcard(text);
        var literal = wildcard < 0 ? text : text[..wildcard];
        if (literal.Length == 0)
            return false;

        var coveredRoot = wildcard < 0 || literal[^1] == Path.DirectorySeparatorChar
            ? literal.TrimEnd(Path.DirectorySeparatorChar)
            : Path.GetDirectoryName(literal);

        var normalized = NormalizeCoveredRoot(coveredRoot);
        return normalized != null && IsSameOrUnder(normalized, excludePath);
    }

    private static string? NormalizeCoveredRoot(string? root)
    {
        if (string.IsNullOrEmpty(root))
            return null;
        try
        {
            var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            // Trimming a drive root leaves the drive-relative "C:", which no resolved path starts with.
            return full.Length == 2 && full[1] == ':' ? full + Path.DirectorySeparatorChar : full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static int IndexOfGlobWildcard(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '*' or '?' or '[')
                return i;
        }

        return -1;
    }

    private static bool IsDevicePath(string text) =>
        text.StartsWith(@"\\?\", StringComparison.Ordinal) || text.StartsWith(@"\\.\", StringComparison.Ordinal);

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
