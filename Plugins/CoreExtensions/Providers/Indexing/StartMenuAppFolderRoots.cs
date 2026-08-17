namespace Lertaro.Plugins.CoreExtensions.Providers.Indexing;

/// <summary>
/// Keeps the folders scanned for application entries and the folders watched for cache invalidation in
/// lockstep. Split from <see cref="StartMenuAppItemProvider"/> to keep that provider below the
/// repository's per-file limit; it has no state of its own.
/// </summary>
internal static class StartMenuAppFolderRoots
{
    internal static IReadOnlyList<string> Merge(
        IEnumerable<string> builtInRoots,
        IEnumerable<string>? customRoots,
        Func<string, bool> directoryExists)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(builtInRoots);
        if (customRoots != null)
            Add(customRoots);
        return roots.ToList();

        void Add(IEnumerable<string> candidates)
        {
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                var path = Environment.ExpandEnvironmentVariables(candidate.Trim());
                if (!directoryExists(path))
                    continue;

                roots.Add(path);
            }
        }
    }
}
