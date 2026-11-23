namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

internal sealed class WalkFilter
{
    private readonly ExclusionRuleSet _globalRules;
    private readonly int _maxDepth;
    private readonly bool _useIgnoreFiles;

    public int WorkerCount { get; }

    private WalkFilter(
        ExclusionRuleSet globalRules,
        int maxDepth,
        int workerCount,
        bool useIgnoreFiles)
    {
        _globalRules = globalRules;
        _maxDepth = Math.Max(0, maxDepth);
        _useIgnoreFiles = useIgnoreFiles;
        WorkerCount = Math.Max(0, workerCount);
    }

    public static WalkFilter Create(string root, WalkOptions options) => new WalkFilter(
            ExclusionRuleSet.From(new UserSettings
            {
                ExcludedPaths = options.ExcludedPaths.ToList(),
                IgnoredPathGlobs = options.IgnoredPathGlobs.ToList(),
                IgnoredPathRegexes = options.IgnoredPathRegexes.ToList()
            }, root),
            options.MaxDepth,
            options.WorkerCount,
            options.UseIgnoreFiles);

    private static readonly string[] IgnoreFileNames = [".ignore", ".fdignore", ".gitignore"];

    // `listing`, when the caller already holds this directory's complete listing, answers "is there an
    // ignore file here" without a File.Exists probe per name -- three extra round trips per directory on
    // a network share, millions of them across a large NAS walk (#305).
    public NetworkIgnoreRuleSet LoadIgnoreRules(string physicalDir, string logicalDir, NetworkIgnoreRuleSet inherited, IReadOnlyList<NativeFileEntry>? listing = null)
    {
        if (!_useIgnoreFiles)
            return inherited;

        var current = inherited;
        foreach (var name in IgnoreFileNames)
        {
            if (listing == null || listing.Any(e => !e.IsDirectory && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                current = LoadIgnoreFile(Path.Combine(physicalDir, name), logicalDir, current);
        }
        return current;
    }

    public bool ShouldIndex(string fullPath, string name, bool isDirectory, FileAttributes attributes, NetworkIgnoreRuleSet ignoreRules)
    {
        if (_globalRules.IsExcludedPath(fullPath, isDirectory))
            return false;

        if (ignoreRules.IsIgnored(fullPath, name, isDirectory))
            return false;

        return true;
    }

    public bool ShouldDescend(string fullPath, FileAttributes attributes, int depth, NetworkIgnoreRuleSet ignoreRules)
    {
        if (_maxDepth > 0 && depth > _maxDepth)
            return false;

        if (_globalRules.IsExcludedPath(fullPath, isDirectory: true))
            return false;

        var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
        if (ignoreRules.IsIgnored(fullPath, name, isDirectory: true))
            return false;

        return true;
    }

    private NetworkIgnoreRuleSet LoadIgnoreFile(string physicalPath, string logicalDir, NetworkIgnoreRuleSet inherited)
    {
        if (!File.Exists(physicalPath))
            return inherited;

        try
        {
            var rules = inherited;
            var basePath = PathHelpers.NormalizePath(logicalDir, true);
            foreach (var rawLine in File.ReadLines(physicalPath))
            {
                var rule = NetworkIgnoreRule.Parse(basePath, rawLine);
                if (rule != null)
                    rules = rules.Add(rule.Value);
            }

            return rules;
        }
        catch
        {
            return inherited;
        }
    }

}
