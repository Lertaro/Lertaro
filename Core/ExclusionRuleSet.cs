using System.Text.RegularExpressions;

using Lertaro.Core.Indexer.NetworkDrive.Walk;
namespace Lertaro.Core;

public sealed class ExclusionRuleSet
{
    private readonly string[] _excludedRoots;
    private readonly NetworkGlobPattern[] _ignoredGlobs;
    private readonly Regex[] _ignoredRegexes;
    private readonly string? _root;

    // Concurrent because the streaming search calls in from its local-drive and network tasks at once
    // (see SearchService.SearchStreamingAsync). Bounded by the number of distinct directories the
    // results live in, and scoped to this instance, so it needs no invalidation of its own.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _ancestorVerdicts =
        new(StringComparer.OrdinalIgnoreCase);

    private ExclusionRuleSet(string[] excludedRoots, NetworkGlobPattern[] ignoredGlobs, Regex[] ignoredRegexes, string? root = null)
    {
        _excludedRoots = excludedRoots;
        _ignoredGlobs = ignoredGlobs;
        _ignoredRegexes = ignoredRegexes;
        _root = root;
    }

    public static ExclusionRuleSet Empty { get; } = new(Array.Empty<string>(), Array.Empty<NetworkGlobPattern>(), Array.Empty<Regex>());

    private static ExclusionRuleSet? _cachedRules;
    private static UserSettings? _cachedSettingsSource;
    private static readonly object _lock = new();

    public static void InvalidateCache()
    {
        lock (_lock)
        {
            _cachedRules = null;
            _cachedSettingsSource = null;
        }
    }

    public static ExclusionRuleSet From(UserSettings settings)
    {
        lock (_lock)
        {
            if (_cachedRules != null && ReferenceEquals(_cachedSettingsSource, settings))
            {
                return _cachedRules;
            }

            _cachedRules = new ExclusionRuleSet(
                BuildExcludedRoots(settings.ExcludedPaths),
                BuildIgnoredGlobs(settings.IgnoredPathGlobs),
                BuildIgnoredRegexes(settings.IgnoredPathRegexes));
            _cachedSettingsSource = settings;
            return _cachedRules;
        }
    }

    public static ExclusionRuleSet From(UserSettings settings, string root) => new(
        BuildExcludedRoots(settings.ExcludedPaths, NormalizePath(root, isDirectory: true)),
        BuildIgnoredGlobs(settings.IgnoredPathGlobs),
        BuildIgnoredRegexes(settings.IgnoredPathRegexes),
        NormalizePath(root, isDirectory: true));

    public bool IsExcluded(SearchResult result, string? exemptRoot = null) => IsExcludedPath(result.Path, result.IsDir, exemptRoot);

    public bool IsExcludedPath(string path, bool isDirectory, string? exemptRoot = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = NormalizePath(path, isDirectory);
        var normalizedExempt = !string.IsNullOrEmpty(exemptRoot) ? NormalizePath(exemptRoot, isDirectory: true) : null;

        // 1. Check excluded roots on the full normalized path
        foreach (var excludedRoot in _excludedRoots)
        {
            if (normalizedExempt != null &&
                (normalizedExempt.StartsWith(excludedRoot, StringComparison.OrdinalIgnoreCase) ||
                 (excludedRoot.StartsWith(normalizedExempt, StringComparison.OrdinalIgnoreCase) && string.Equals(normalized, excludedRoot, StringComparison.OrdinalIgnoreCase))))
                continue;

            if (normalized.StartsWith(excludedRoot, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (_ignoredGlobs.Length == 0 && _ignoredRegexes.Length == 0)
            return false;

        // 2. Check globs and regexes on the path itself, then on its ancestors -- which are cached,
        // because an ancestor's verdict belongs to the ancestor rather than to whatever is under it.
        //
        // This used to re-walk every parent of every result. With five globs configured and paths
        // averaging ninety characters that is roughly a hundred and twenty regex evaluations and two
        // dozen string allocations PER RESULT, and the full window now asks about every match on the
        // drive: measured at 30us a result, 20 seconds of one pegged core for 660k of them, against
        // 0.26 seconds for the same set with no globs. Nearly all of it was re-deciding the same few
        // tens of thousands of directories over and over -- the hundreds of thousands of files under one
        // directory all get the same answer from it.
        var leaf = normalized.TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(leaf))
            return false;
        if (MatchesIgnorePatterns(normalized, leaf))
            return true;

        var parentDirectory = Path.GetDirectoryName(leaf);
        return !string.IsNullOrEmpty(parentDirectory) && AncestorIsIgnored(parentDirectory);
    }

    /// <summary>
    /// Whether <paramref name="directory"/> or anything above it matches an ignore pattern, memoised
    /// per directory for the lifetime of this rule set (which is immutable -- a settings change builds a
    /// new one, see <see cref="From(UserSettings)"/> and <see cref="InvalidateCache"/>).
    /// </summary>
    private bool AncestorIsIgnored(string directory)
    {
        if (_ancestorVerdicts.TryGetValue(directory, out var known))
            return known;

        // Walks up collecting the levels it has to decide, then writes the answer back to all of them
        // rather than only the one asked about, so a sibling deeper in the same tree gets a hit too.
        // Iterative rather than recursive: a malformed parent chain has produced an unbounded walk in
        // this codebase before, and a list can't overflow the stack the way that did.
        List<string>? undecided = null;
        var current = directory;
        var verdict = false;
        while (true)
        {
            if (_ancestorVerdicts.TryGetValue(current, out verdict))
                break;

            (undecided ??= new List<string>()).Add(current);

            // Matched here, so every level collected below it is excluded too -- they are all its
            // descendants.
            if (MatchesIgnorePatterns(current + Path.DirectorySeparatorChar, current))
            {
                verdict = true;
                break;
            }

            var parent = Path.GetDirectoryName(current);
            // Length rather than inequality: an entry whose parent doesn't strictly shorten the path
            // isn't a parent, and treating it as one is what makes the walk unbounded.
            if (string.IsNullOrEmpty(parent) || parent.Length >= current.Length)
            {
                verdict = false;
                break;
            }
            current = parent;
        }

        if (undecided != null)
            foreach (var level in undecided)
                _ancestorVerdicts[level] = verdict;

        return verdict;
    }

    // currentWithSeparator only differs from pathForGlob for a drive root, where GetRelativePath's
    // StartsWith(_root) check needs the separator to recognise the root as the root.
    private bool MatchesIgnorePatterns(string currentWithSeparator, string pathForGlob)
    {
        var relativePath = GetRelativePath(currentWithSeparator);
        var slashPath = pathForGlob.Replace('\\', '/');

        foreach (var glob in _ignoredGlobs)
        {
            if (glob.IsMatch(pathForGlob) || glob.IsMatch(slashPath) || glob.IsMatch(relativePath))
                return true;
        }

        if (_ignoredRegexes.Length == 0)
            return false;

        var name = Path.GetFileName(pathForGlob);
        foreach (var regex in _ignoredRegexes)
        {
            if (regex.IsMatch(name) || regex.IsMatch(pathForGlob) || regex.IsMatch(slashPath) || regex.IsMatch(relativePath))
                return true;
        }

        return false;
    }

    private static string[] BuildExcludedRoots(IReadOnlyList<string> paths)
        => BuildExcludedRoots(paths, root: null);

    private static string[] BuildExcludedRoots(IReadOnlyList<string> paths, string? root)
    {
        if (paths.Count == 0)
            return Array.Empty<string>();

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizePath(Environment.ExpandEnvironmentVariables(path), isDirectory: true))
            .Where(path => root == null || path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static NetworkGlobPattern[] BuildIgnoredGlobs(IReadOnlyList<string> globs)
    {
        if (globs.Count == 0)
            return Array.Empty<NetworkGlobPattern>();

        return globs
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => GlobMatcher.Compile(pattern.Trim()))
            .Where(pattern => !pattern.IsEmpty)
            .ToArray();
    }

    private static Regex[] BuildIgnoredRegexes(IReadOnlyList<string> regexes)
    {
        if (regexes.Count == 0)
            return Array.Empty<Regex>();

        var compiled = new List<Regex>();
        foreach (var pattern in regexes)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            try
            {
                compiled.Add(new Regex(
                    pattern.Trim(),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
                    TimeSpan.FromMilliseconds(50)));
            }
            catch (ArgumentException ex)
            {
                Logger.Log($"[ExclusionRuleSet] Invalid exclude regex '{pattern}': {ex.Message}", LogLevel.Warn);
            }
        }

        return compiled.ToArray();
    }

    private static string NormalizePath(string path, bool isDirectory)
    {
        var normalized = path.Trim().Trim('"')
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch
        {
        }

        return isDirectory
            ? normalized.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar
            : normalized;
    }

    private string GetRelativePath(string normalizedPath)
    {
        if (_root == null || !normalizedPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            return normalizedPath.TrimEnd(Path.DirectorySeparatorChar);

        return normalizedPath[_root.Length..].TrimEnd(Path.DirectorySeparatorChar);
    }
}
