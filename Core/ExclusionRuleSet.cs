using System.Text.RegularExpressions;

using Lertaro.Core.Indexer.NetworkDrive.Walk;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Core;

public sealed class ExclusionRuleSet
{
    private const int MaxPriorityAncestorDepth = 64;

    private static readonly RegexOptions CompiledOptions =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private readonly string[] _excludedRoots;
    private readonly NetworkGlobPattern[] _ignoredGlobs;
    private readonly Regex[] _ignoredRegexes;

    private readonly EffectivePathRule[] _priorityPaths;
    private readonly EffectiveGlobRule[] _priorityGlobs;
    private readonly EffectiveRegexRule[] _priorityRegexes;

    private readonly string? _root;

    /// <summary>
    /// Whether any pattern can tell the two separator spellings apart, and so whether the slash form of a
    /// path is worth building at all. See the constructor for why a backslash-spelled glob cannot.
    /// </summary>
    private readonly bool _anyPatternCanTellSeparatorsApart;

    // Concurrent because the streaming search calls in from its local-drive and network tasks at once
    // (see SearchService.SearchStreamingAsync). Bounded by the number of distinct directories the
    // results live in, and scoped to this instance, so it needs no invalidation of its own.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _ancestorVerdicts =
        new(StringComparer.OrdinalIgnoreCase);

    // Same lifetime and rationale: a directory's priority is decided once and reused by every file under
    // it, which is what keeps the ranking key affordable on a result set of a hundred thousand rows.
    // Values pack the inherited path and pattern components; see Encode/Decode.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _priorityVerdicts =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct EffectivePathRule(string Root, IndexRuleEffect Effect, bool AlwaysTop);
    private readonly record struct EffectiveGlobRule(NetworkGlobPattern Pattern, IndexRuleEffect Effect, bool AlwaysTop);
    private readonly record struct EffectiveRegexRule(Regex Pattern, IndexRuleEffect Effect, bool AlwaysTop);

    /// <summary>
    /// What the rules say about one path: which bucket it lands in, and whether a high rule pins it to the
    /// very top. <see cref="AlwaysTop"/> is only ever true together with <see cref="IndexRuleEffect.High"/>;
    /// build every value through <see cref="Verdict"/> so that invariant cannot be broken by a merge.
    /// </summary>
    public readonly record struct PriorityVerdict(IndexRuleEffect Effect, bool AlwaysTop);

    private ExclusionRuleSet(IReadOnlyList<IndexRuleSetting> rules, string? root = null)
    {
        _root = root;

        var excludedRoots = new List<string>();
        var ignoredGlobs = new List<NetworkGlobPattern>();
        var ignoredRegexes = new List<Regex>();
        var priorityPaths = new List<EffectivePathRule>();
        var priorityGlobs = new List<EffectiveGlobRule>();
        var priorityRegexes = new List<EffectiveRegexRule>();

        foreach (var rule in rules)
        {
            if (!IndexRuleSyntax.TryParse(rule.Pattern, rule.Effect, rule.AlwaysTop, out var parsed, out var error))
            {
                Logger.Log($"[ExclusionRuleSet] Ignoring invalid rule '{rule.Pattern}': {error}", LogLevel.Warn);
                continue;
            }

            switch (parsed.Kind)
            {
                case IndexRuleKind.Path:
                {
                    if (!IndexRuleSyntax.TryResolvePath(parsed.Pattern, out var resolved, out error))
                    {
                        Logger.Log($"[ExclusionRuleSet] Ignoring unresolved path rule '{rule.Pattern}': {error}", LogLevel.Warn);
                        continue;
                    }

                    var normalizedRoot = NormalizePath(resolved, isDirectory: true);
                    if (_root != null && !normalizedRoot.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (parsed.Effect == IndexRuleEffect.Exclude)
                        excludedRoots.Add(normalizedRoot);
                    else
                        priorityPaths.Add(new EffectivePathRule(
                            normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), parsed.Effect, parsed.AlwaysTop));
                    break;
                }

                case IndexRuleKind.Glob:
                {
                    var glob = GlobMatcher.Compile(UserPathResolver.Expand(parsed.Pattern));
                    if (glob.IsEmpty)
                        continue;

                    if (parsed.Effect == IndexRuleEffect.Exclude)
                        ignoredGlobs.Add(glob);
                    else
                        priorityGlobs.Add(new EffectiveGlobRule(glob, parsed.Effect, parsed.AlwaysTop));
                    break;
                }

                default:
                {
                    Regex regex;
                    try
                    {
                        regex = new Regex(parsed.Pattern, CompiledOptions, TimeSpan.FromMilliseconds(50));
                    }
                    catch (ArgumentException ex)
                    {
                        Logger.Log($"[ExclusionRuleSet] Invalid regex rule '{rule.Pattern}': {ex.Message}", LogLevel.Warn);
                        continue;
                    }

                    if (parsed.Effect == IndexRuleEffect.Exclude)
                        ignoredRegexes.Add(regex);
                    else
                        priorityRegexes.Add(new EffectiveRegexRule(regex, parsed.Effect, parsed.AlwaysTop));
                    break;
                }
            }
        }

        _excludedRoots = excludedRoots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _ignoredGlobs = ignoredGlobs.ToArray();
        _ignoredRegexes = ignoredRegexes.ToArray();
        _priorityPaths = priorityPaths.ToArray();
        _priorityGlobs = priorityGlobs.ToArray();
        _priorityRegexes = priorityRegexes.ToArray();
        HighPriorityRoots = _priorityPaths
            .Where(rule => rule.Effect == IndexRuleEffect.High)
            .Select(rule => rule.Root)
            .Where(root => root.Length > 3 && !string.Equals(Path.GetPathRoot(root), root, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // A glob only cares which spelling a separator took if it can say something about one: GlobToRegex
        // normalizes the pattern's separators and compiles each to a class matching either spelling, so a
        // plain `*\Cache\*` answers the same on both forms. Inside a character class the characters go out
        // verbatim, and a user regex reaches the matcher untouched -- both are the cases that keep the
        // converted form in play. Everything else gets to skip building it per result.
        _anyPatternCanTellSeparatorsApart =
            _ignoredGlobs.Any(g => g.RawPattern.Contains('/') || g.RawPattern.Contains('['))
            || _ignoredRegexes.Any(r => r.ToString().Contains('/'));
    }

    public static ExclusionRuleSet Empty { get; } = new(Array.Empty<IndexRuleSetting>());

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

            _cachedRules = new ExclusionRuleSet(IndexRuleMigration.Ensure(settings));
            _cachedSettingsSource = settings;
            return _cachedRules;
        }
    }

    public static ExclusionRuleSet From(UserSettings settings, string root) => new(
        IndexRuleMigration.Ensure(settings),
        NormalizePath(root, isDirectory: true));

    public bool IsExcluded(SearchResult result, string? exemptRoot = null)
    {
        if (string.IsNullOrWhiteSpace(result.Path))
            return false;

        // Indexed paths are already absolute and canonical. Path.GetFullPath can ask Windows to expand
        // short names, turning the per-result exclusion check into filesystem I/O for large searches.
        var normalized = NormalizeIndexedPath(result.Path, result.IsDir);
        var normalizedExempt = !string.IsNullOrEmpty(exemptRoot) ? NormalizePath(exemptRoot, isDirectory: true) : null;
        return IsExcludedNormalized(normalized, normalizedExempt);
    }

    public bool IsExcludedPath(string path, bool isDirectory, string? exemptRoot = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return IsExcludedNormalized(
            NormalizePath(path, isDirectory),
            !string.IsNullOrEmpty(exemptRoot) ? NormalizePath(exemptRoot, isDirectory: true) : null);
    }

    /// <summary>
    /// The low/high priority of a result. Files inherit the nearest rule-bearing ancestor; at one level,
    /// high beats low beats normal. No matching rule is normal, and a normal path rule replaces a shallower
    /// path rule so a subdirectory can opt back into the default (pattern rules still merge along the chain).
    /// </summary>
    public IndexRuleEffect GetPriority(SearchResult result) => result == null
        ? IndexRuleEffect.Normal
        : GetPriority(result.Path, result.IsDir);

    /// <summary>Whether any high-priority rule exists. Callers use it to widen the candidate window:</summary>
    /// <remarks>
    /// High-priority rows must be able to outrank everything else, but the engine truncates before the App
    /// ranks. Without high rules the extra rows would only cost time, so the widening is gated on them.
    /// </remarks>
    public bool HasHighPriorityRules =>
        _priorityPaths.Any(rule => rule.Effect == IndexRuleEffect.High)
        || _priorityGlobs.Any(rule => rule.Effect == IndexRuleEffect.High)
        || _priorityRegexes.Any(rule => rule.Effect == IndexRuleEffect.High);

    /// <summary>
    /// Directories whose contents a high path rule promotes. The quick window searches each of them on
    /// its own so a high-priority file cannot be crowded out of the shared candidate pool before ranking.
    /// </summary>
    /// <remarks>
    /// Only whole paths can be turned into a scoped search: globs and regexes describe a name shape, not a
    /// subtree, so their rows still depend on the regular pool -- a pinned pattern rule therefore adds no
    /// root here either, and the supplemental search behaves exactly as before. Drive roots are skipped --
    /// promoting a whole volume says nothing and would make every query scan it twice.
    /// </remarks>
    public IReadOnlyList<string> HighPriorityRoots { get; }

    public IndexRuleEffect GetPriority(string path, bool isDirectory) => GetPriorityVerdict(path, isDirectory).Effect;

    /// <summary>
    /// Bucket and pin of one path. The two components are kept apart until the last step, because they
    /// inherit differently: the nearest path rule replaces every path rule above it wholesale, while
    /// pattern rules accumulate their strongest effect along the chain -- and a path rule that ties with a
    /// pattern rule decides the pin, so a folder can switch a global pinned pattern off.
    /// </summary>
    public PriorityVerdict GetPriorityVerdict(string path, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Verdict(IndexRuleEffect.Normal, alwaysTop: false);
        if (_priorityPaths.Length == 0 && _priorityGlobs.Length == 0 && _priorityRegexes.Length == 0)
            return Verdict(IndexRuleEffect.Normal, alwaysTop: false);

        var normalized = NormalizeIndexedPath(path, isDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (normalized.Length == 0)
            return Verdict(IndexRuleEffect.Normal, alwaysTop: false);

        // Own-level patterns matter (a `*.exe` high rule must beat a low folder), but a path rule on an
        // ancestor is a deliberate folder-level decision and the nearest one wins over shallower paths.
        var pathPart = PathPriorityAt(normalized);
        var patternPart = PatternPriorityAt(normalized);

        var parent = Path.GetDirectoryName(normalized);
        if (!string.IsNullOrEmpty(parent))
        {
            InheritedPriority(parent, out var inheritedPath, out var inheritedPattern);
            pathPart ??= inheritedPath;
            patternPart = Strongest(patternPart, inheritedPattern);
        }

        return PreferPath(pathPart, patternPart) ?? Verdict(IndexRuleEffect.Normal, alwaysTop: false);
    }

    /// <summary>
    /// The two components a child of <paramref name="directory"/> inherits: the nearest path rule at or above
    /// it plus the best pattern hit along the chain. Cached per directory, because every file below it
    /// shares the answer.
    /// </summary>
    private void InheritedPriority(string directory, out PriorityVerdict? pathPart, out PriorityVerdict? patternPart)
    {
        var undecided = new List<string>();
        PriorityVerdict? inheritedPath = null;
        PriorityVerdict? inheritedPattern = null;

        var current = directory;
        var depth = 0;
        while (true)
        {
            if (_priorityVerdicts.TryGetValue(current, out var known))
            {
                Decode(known, out inheritedPath, out inheritedPattern);
                break;
            }

            undecided.Add(current);

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent.Length >= current.Length || ++depth >= MaxPriorityAncestorDepth)
                break;
            current = parent;
        }

        // Fill from the topmost undecided level down, so each level sees what its parent contributed.
        for (var i = undecided.Count - 1; i >= 0; i--)
        {
            var level = undecided[i];
            // A path rule closer to the child replaces the inherited one even when it is weaker, and even
            // for the pin: an unpinned High on this folder is how the user switches an ancestor's pin off.
            inheritedPath = PathPriorityAt(level) ?? inheritedPath;
            inheritedPattern = Strongest(inheritedPattern, PatternPriorityAt(level));
            _priorityVerdicts[level] = Encode(inheritedPath, inheritedPattern);
        }

        pathPart = inheritedPath;
        patternPart = inheritedPattern;
    }

    // A pin is only ever carried by High, so every verdict goes through here.
    private static PriorityVerdict Verdict(IndexRuleEffect effect, bool alwaysTop) =>
        new(effect, alwaysTop && effect == IndexRuleEffect.High);

    /// <summary>
    /// Merge of two rules of the same kind -- pattern rules along the chain, or several rules naming one
    /// level. The stronger bucket wins; on an equal bucket the pin survives if either side asks for it, so
    /// an unpinned rule can never cancel a pinned one of the same strength.
    /// </summary>
    private static PriorityVerdict? Strongest(PriorityVerdict? current, PriorityVerdict? candidate)
    {
        if (candidate == null)
            return current;
        if (current == null)
            return candidate;

        var byStrength = PriorityStrength(candidate.Value.Effect).CompareTo(PriorityStrength(current.Value.Effect));
        if (byStrength > 0)
            return candidate;
        if (byStrength < 0)
            return current;
        return Verdict(current.Value.Effect, current.Value.AlwaysTop || candidate.Value.AlwaysTop);
    }

    /// <summary>
    /// Merge of a path rule against a pattern rule. The stronger bucket wins, and on an equal bucket the
    /// PATH rule decides -- pin included, no OR here. A path rule is an explicit call about one folder, so
    /// `C:\work` High-unpinned must outrank a global `*.ps1` High-pinned; otherwise a user could not turn
    /// pinning off for a folder without editing the pattern.
    /// </summary>
    private static PriorityVerdict? PreferPath(PriorityVerdict? pathRule, PriorityVerdict? patternRule)
    {
        if (pathRule == null)
            return patternRule;
        if (patternRule == null)
            return pathRule;

        return PriorityStrength(patternRule.Value.Effect) > PriorityStrength(pathRule.Value.Effect)
            ? patternRule
            : pathRule;
    }

    // Packs "effect and pin, or none" into 4 bits per component: 0 means no rule, and a real bucket packs to
    // ((1 + effect) << 1) | pin, which lands in 4..9 because a priority rule is never Exclude. Two
    // components share one int so the dictionary can cache a miss (-1 was not enough: a path rule and a
    // pattern rule contribute independently).
    private static int Pack(PriorityVerdict? verdict) =>
        verdict == null ? 0 : ((1 + (int)verdict.Value.Effect) << 1) | (verdict.Value.AlwaysTop ? 1 : 0);

    private static PriorityVerdict? Unpack(int packed) => packed == 0
        ? null
        : Verdict((IndexRuleEffect)((packed >> 1) - 1), (packed & 1) != 0);

    private static int Encode(PriorityVerdict? path, PriorityVerdict? pattern) =>
        (Pack(path) << 4) | Pack(pattern);

    private static void Decode(int code, out PriorityVerdict? path, out PriorityVerdict? pattern)
    {
        path = Unpack(code >> 4);
        pattern = Unpack(code & 0xF);
    }

    /// <summary>Exact path rules at one level; null when no path rule names it.</summary>
    private PriorityVerdict? PathPriorityAt(string fullPath)
    {
        PriorityVerdict? matched = null;
        foreach (var rule in _priorityPaths)
        {
            if (fullPath.Equals(rule.Root, StringComparison.OrdinalIgnoreCase))
                matched = Strongest(matched, Verdict(rule.Effect, rule.AlwaysTop));
        }
        return matched;
    }

    /// <summary>
    /// Glob and regex rules at one level, ignoring path rules and ancestors. Priority regexes test only the
    /// leaf and the search-root-relative path, never the absolute path: otherwise every rule would also see
    /// the drive and parent names.
    /// </summary>
    private PriorityVerdict? PatternPriorityAt(string fullPath)
    {
        if (_priorityGlobs.Length == 0 && _priorityRegexes.Length == 0)
            return null;

        PriorityVerdict? matched = null;
        var name = Path.GetFileName(fullPath);
        var relative = GetPriorityRelativePath(fullPath);
        var withSeparator = fullPath + Path.DirectorySeparatorChar;
        var relativeWithSeparator = relative.Length == 0 ? relative : relative + Path.DirectorySeparatorChar;

        foreach (var rule in _priorityGlobs)
        {
            if (rule.Pattern.IsMatch(fullPath)
                || rule.Pattern.IsMatch(withSeparator)
                || (relative.Length > 0 && (rule.Pattern.IsMatch(relative) || rule.Pattern.IsMatch(relativeWithSeparator)))
                || rule.Pattern.IsMatch(name))
            {
                matched = Strongest(matched, Verdict(rule.Effect, rule.AlwaysTop));
            }
        }

        foreach (var rule in _priorityRegexes)
        {
            if (rule.Pattern.IsMatch(name)
                || (relative.Length > 0 && (rule.Pattern.IsMatch(relative) || rule.Pattern.IsMatch(relativeWithSeparator))))
            {
                matched = Strongest(matched, Verdict(rule.Effect, rule.AlwaysTop));
            }
        }

        return matched;
    }

    private static int PriorityStrength(IndexRuleEffect effect) => effect switch
    {
        IndexRuleEffect.High => 2,
        IndexRuleEffect.Low => 1,
        _ => 0,
    };

    private static bool IsSameOrUnder(string path, string root)
    {
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase))
            return true;
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private string GetPriorityRelativePath(string fullPath)
    {
        if (_root != null && fullPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            return fullPath[_root.Length..].Trim(Path.DirectorySeparatorChar);

        // A bare drive root has no relative part; returning "C:\" would let a `^C:` regex match it.
        var pathRoot = Path.GetPathRoot(fullPath);
        if (pathRoot == null)
            return fullPath;
        return fullPath.Length > pathRoot.Length
            ? fullPath[pathRoot.Length..].Trim(Path.DirectorySeparatorChar)
            : "";
    }

    private bool IsExcludedNormalized(string normalized, string? normalizedExempt)
    {
        // 1. Check excluded roots on the full normalized path
        foreach (var excludedRoot in _excludedRoots)
        {
            // An exempt root re-includes only what is actually inside the subtree the caller pointed at:
            // either the result sits under an exempt root that itself sits under this excluded root, or the
            // result IS this excluded root while the exempt subtree is its ancestor (the folder's own row is
            // not "content under the excluded root"). Without the containment check the first case would
            // also re-include results elsewhere under the excluded root.
            if (normalizedExempt != null &&
                normalized.StartsWith(normalizedExempt, StringComparison.OrdinalIgnoreCase) &&
                (normalizedExempt.StartsWith(excludedRoot, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(normalized, excludedRoot, StringComparison.OrdinalIgnoreCase)))
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

    internal static string NormalizeIndexedPath(string path, bool isDirectory)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return isDirectory && !Path.EndsInDirectorySeparator(normalized)
            ? normalized + Path.DirectorySeparatorChar
            : normalized;
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
    //
    // The forms are an OR per pattern, so testing them in the cheapest order first costs a pattern an
    // extra comparison at most and changes no answer: the path as the index holds it, then its path
    // relative to the search root, then -- only when some pattern can tell the two separator spellings
    // apart, see _anyPatternCanTellSeparatorsApart -- the same path with '/' separators, built once.
    private bool MatchesIgnorePatterns(string currentWithSeparator, string pathForGlob)
    {
        var relativePath = GetRelativePath(currentWithSeparator);
        string? slashPath = null;

        foreach (var glob in _ignoredGlobs)
        {
            if (glob.IsMatch(pathForGlob) || glob.IsMatch(relativePath))
                return true;
            if (_anyPatternCanTellSeparatorsApart && glob.IsMatch(slashPath ??= pathForGlob.Replace('\\', '/')))
                return true;
        }

        if (_ignoredRegexes.Length == 0)
            return false;

        var name = Path.GetFileName(pathForGlob);
        foreach (var regex in _ignoredRegexes)
        {
            if (regex.IsMatch(name) || regex.IsMatch(pathForGlob) || regex.IsMatch(relativePath))
                return true;
            if (_anyPatternCanTellSeparatorsApart && regex.IsMatch(slashPath ??= pathForGlob.Replace('\\', '/')))
                return true;
        }

        return false;
    }

    private static string NormalizePath(string path, bool isDirectory)
    {
        var normalized = path.Trim().Trim('"')
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        try
        {
            if (!WslPath.IsPath(normalized))
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
