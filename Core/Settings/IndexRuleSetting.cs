using System.Text.RegularExpressions;

using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Core;

/// <summary>What a rule does to a matching path: hide it, or move it within its result-type group.</summary>
public enum IndexRuleEffect
{
    Exclude = 0,
    Low = 1,
    Normal = 2,
    High = 3,
}

public enum IndexRuleKind
{
    Path,
    Glob,
    Regex,
}

public enum IndexRuleError
{
    None,
    Empty,
    EmptyRegex,
    InvalidRegex,
    UnresolvedEnvironmentVariable,
    VirtualPathWithoutFileSystem,
    InvalidPath,
}

/// <summary>
/// One index rule as persisted. The kind is not stored: it is derived from the pattern's spelling by
/// <see cref="IndexRuleSyntax"/> every time the rule set is built, so a rule can never disagree with
/// the way it is matched.
/// </summary>
public sealed class IndexRuleSetting
{
    public string Pattern { get; set; } = string.Empty;
    public IndexRuleEffect Effect { get; set; } = IndexRuleEffect.Normal;

    /// <summary>
    /// Pins every match of a high rule to the very top of the result list. Only meaningful together with
    /// <see cref="IndexRuleEffect.High"/>: the settings page clears the flag when the bucket changes away
    /// from High, and parsing drops it for every other bucket, so no migration of old JSON is needed --
    /// a stored file without the field reads as false.
    /// </summary>
    public bool AlwaysTop { get; set; }
}

/// <summary>
/// A rule after cleaning, classification and (for paths) environment/virtual-path resolution.
/// <see cref="AlwaysTop"/> is already normalized: it is true only for a High rule the user pinned.
/// </summary>
public readonly record struct ParsedIndexRule(
    string Text,
    IndexRuleKind Kind,
    string Pattern,
    IndexRuleEffect Effect,
    bool AlwaysTop = false);

/// <summary>
/// The single place that decides what a user typed: a complete path, a glob, or a delimited regex.
/// </summary>
/// <remarks>
/// The order matters. Quotes and outer whitespace come off first, then the regex delimiter is checked
/// before any separator conversion -- otherwise <c>/^\./</c> would turn into <c>\^\.</c> and stop being a
/// regex -- and only then are '/' separators normalized and trailing separators removed. Complete-path
/// detection is spelling-only: the path does not have to exist, which keeps rule editing usable for
/// directories that are not currently mounted.
/// </remarks>
public static class IndexRuleSyntax
{
    private const int MaxQuoteLayers = 3;

    // Matches ExclusionRuleSet's existing budget. A rule that cannot answer within it is skipped by the
    // engine (and abandoned for the session, for globs).
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly RegexOptions CompiledOptions =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex DriveRootPattern = new(@"^[A-Za-z]:\\", RegexOptions.CultureInvariant);
    private static readonly Regex DriveOnlyPattern = new(@"^[A-Za-z]:$", RegexOptions.CultureInvariant);
    private static readonly Regex EnvironmentPrefixPattern = new(@"^%[^%\\]+%($|\\)", RegexOptions.CultureInvariant);
    private static readonly Regex EnvironmentTokenPattern = new(@"%[^%]+%", RegexOptions.CultureInvariant);
    private static readonly Regex VirtualTokenPattern =
        new(@"^(shell:|::)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Trims whitespace and up to three layers of matching quotes.</summary>
    public static string Clean(string? input)
    {
        var text = (input ?? string.Empty).Trim();
        for (var layer = 0; layer < MaxQuoteLayers; layer++)
        {
            if (text.Length < 2)
                break;
            var first = text[0];
            var last = text[^1];
            if ((first is '"' or '\'') && first == last)
            {
                text = text[1..^1].Trim();
                continue;
            }
            break;
        }
        return text;
    }

    public static bool TryParse(string? input, IndexRuleEffect effect, out ParsedIndexRule rule, out IndexRuleError error)
        => TryParse(input, effect, alwaysTop: false, out rule, out error);

    /// <param name="alwaysTop">The pin requested by the rule. It is normalized here and nowhere else:
    /// only a High rule can pin, so Normal, Low and Exclude carry it as false.</param>
    public static bool TryParse(string? input, IndexRuleEffect effect, bool alwaysTop, out ParsedIndexRule rule, out IndexRuleError error)
    {
        var pinned = alwaysTop && effect == IndexRuleEffect.High;
        rule = default;
        error = IndexRuleError.None;

        var text = Clean(input);
        if (text.Length == 0)
        {
            error = IndexRuleError.Empty;
            return false;
        }

        // Regex is checked before separator conversion: its delimiters are '/' and its body is opaque.
        if (text.Length >= 2 && text[0] == '/' && text[^1] == '/')
        {
            var body = text[1..^1];
            if (body.Length == 0)
            {
                error = IndexRuleError.EmptyRegex;
                return false;
            }
            if (!TryCompileRegex(body))
            {
                error = IndexRuleError.InvalidRegex;
                return false;
            }
            rule = new ParsedIndexRule(text, IndexRuleKind.Regex, body, effect, pinned);
            return true;
        }

        if (text.Contains('/'))
            text = text.Replace('/', '\\');

        text = TrimTrailingSeparators(text);
        if (text.Length == 0)
        {
            error = IndexRuleError.Empty;
            return false;
        }

        var kind = HasWildcard(text) ? IndexRuleKind.Glob
            : IsCompletePath(text) ? IndexRuleKind.Path
            : IndexRuleKind.Glob;

        rule = new ParsedIndexRule(text, kind, text, effect, pinned);
        return true;
    }

    public static bool TryCompileRegex(string body)
    {
        try
        {
            _ = new Regex(body, CompiledOptions, MatchTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves a classified path rule to an absolute filesystem path. Environment variables are expanded
    /// first; a <c>shell:</c> or <c>::</c> token is resolved only as far as its first separator, because
    /// the shell helper understands one token and not a token plus subdirectory.
    /// </summary>
    public static bool TryResolvePath(string pattern, out string resolved, out IndexRuleError error)
    {
        resolved = pattern;
        error = IndexRuleError.None;

        var text = Clean(pattern);
        var expanded = UserPathResolver.Expand(text);
        if (text.Contains('%') && EnvironmentTokenPattern.IsMatch(expanded))
        {
            error = IndexRuleError.UnresolvedEnvironmentVariable;
            return false;
        }

        if (VirtualTokenPattern.IsMatch(expanded))
        {
            var separator = expanded.IndexOf('\\');
            var token = separator < 0 ? expanded : expanded[..separator];
            var suffix = separator < 0 ? string.Empty : expanded[separator..];
            var resolvedToken = ShellPathHelper.TryResolveVirtualPath(token);
            if (ShellPathHelper.IsVirtualShellPath(resolvedToken))
            {
                error = IndexRuleError.VirtualPathWithoutFileSystem;
                return false;
            }
            expanded = resolvedToken + suffix;
        }

        try
        {
            if (!WslPath.IsPath(expanded))
                expanded = Path.GetFullPath(expanded);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = IndexRuleError.InvalidPath;
            return false;
        }

        resolved = expanded;
        return true;
    }

    /// <summary>True when the spelling is that of an absolute path, whether or not it exists.</summary>
    public static bool IsCompletePath(string text)
    {
        if (DriveRootPattern.IsMatch(text) || DriveOnlyPattern.IsMatch(text))
            return true;
        if (text.StartsWith(@"\\", StringComparison.Ordinal))
            return true;
        if (EnvironmentPrefixPattern.IsMatch(text))
            return true;
        return VirtualTokenPattern.IsMatch(text);
    }

    private static bool HasWildcard(string text)
    {
        // A \\?\ or \\?\UNC\ device path starts with a literal '?' that is not a wildcard.
        var start = text.StartsWith(@"\\?\", StringComparison.Ordinal) ? 4 : 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] is '*' or '?')
                return true;
        }
        return false;
    }

    private static string TrimTrailingSeparators(string text)
    {
        var trimmed = text.TrimEnd('\\');
        if (trimmed.Length == 0)
            return text;
        // A bare drive letter is a root ("C:" -> "C:\"); anything else loses its trailing separator.
        if (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
            return trimmed + '\\';
        return trimmed;
    }
}
