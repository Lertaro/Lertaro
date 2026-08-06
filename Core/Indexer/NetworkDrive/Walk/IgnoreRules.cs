namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

internal readonly struct NetworkIgnoreRuleSet
{
    public static readonly NetworkIgnoreRuleSet Empty = new(Array.Empty<NetworkIgnoreRule>());
    private readonly NetworkIgnoreRule[] _rules;

    private NetworkIgnoreRuleSet(NetworkIgnoreRule[] rules) => _rules = rules;

    public NetworkIgnoreRuleSet Add(NetworkIgnoreRule rule)
    {
        var next = new NetworkIgnoreRule[_rules.Length + 1];
        Array.Copy(_rules, next, _rules.Length);
        next[^1] = rule;
        return new NetworkIgnoreRuleSet(next);
    }

    public bool IsIgnored(string fullPath, string name, bool isDirectory)
    {
        var ignored = false;
        foreach (var rule in _rules)
        {
            if (rule.Matches(fullPath, name, isDirectory))
                ignored = !rule.Negated;
        }

        return ignored;
    }
}

internal readonly struct NetworkIgnoreRule
{
    private readonly NetworkGlobPattern _pattern;

    private NetworkIgnoreRule(
        string basePath,
        string pattern,
        bool negated,
        bool directoryOnly,
        bool anchored)
    {
        BasePath = basePath;
        Pattern = pattern;
        Negated = negated;
        DirectoryOnly = directoryOnly;
        Anchored = anchored;
        _pattern = GlobMatcher.Compile(pattern);
    }

    public string BasePath { get; }
    public string Pattern { get; }
    public bool Negated { get; }
    public bool DirectoryOnly { get; }
    public bool Anchored { get; }

    public static NetworkIgnoreRule? Parse(string basePath, string line)
    {
        var pattern = line.Trim();
        if (pattern.Length == 0 || pattern.StartsWith("#", StringComparison.Ordinal))
            return null;

        var negated = pattern.StartsWith("!", StringComparison.Ordinal);
        if (negated)
            pattern = pattern.Substring(1).Trim();

        if (pattern.Length == 0)
            return null;

        var directoryOnly = pattern.EndsWith("/", StringComparison.Ordinal) ||
                             pattern.EndsWith("\\", StringComparison.Ordinal);
        var anchored = pattern.StartsWith("/", StringComparison.Ordinal) ||
                        pattern.StartsWith("\\", StringComparison.Ordinal);
        pattern = pattern.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        return new NetworkIgnoreRule(basePath, pattern, negated, directoryOnly, anchored);
    }

    public bool Matches(string fullPath, string name, bool isDirectory)
    {
        if (DirectoryOnly && !isDirectory)
            return false;

        var normalized = PathHelpers.NormalizePath(fullPath, isDirectory);
        if (!normalized.StartsWith(BasePath, StringComparison.OrdinalIgnoreCase))
            return false;

        var relative = normalized.Substring(BasePath.Length).TrimEnd(Path.DirectorySeparatorChar);
        if (relative.Length == 0)
            return false;

        return _pattern.IsMatch(relative);
    }
}
