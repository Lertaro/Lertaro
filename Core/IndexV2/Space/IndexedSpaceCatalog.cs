namespace Lertaro.Core.IndexV2.Space;

/// <summary>Discovers persisted Lertaro indexes without enumerating any indexed filesystem content.</summary>
public sealed class IndexedSpaceCatalog : IDisposable
{
    private readonly List<IndexedSpaceSource> _sources;

    private IndexedSpaceCatalog(List<IndexedSpaceSource> sources) => _sources = sources;

    public IReadOnlyList<IndexedSpaceSource> Sources => _sources;

    public static IndexedSpaceCatalog OpenDefault()
    {
        var byRoot = new Dictionary<string, IndexedSpaceSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateCacheFiles())
        {
            IndexedSpaceSource source;
            try
            {
                source = IndexedSpaceSource.Open(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                continue;
            }

            if (byRoot.TryGetValue(source.RootPath, out var existing))
            {
                if (existing.LastUpdated >= source.LastUpdated)
                {
                    source.Dispose();
                    continue;
                }
                existing.Dispose();
            }
            byRoot[source.RootPath] = source;
        }

        return new IndexedSpaceCatalog(byRoot.Values
            .OrderBy(source => source.RootPath, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    private static IEnumerable<string> EnumerateCacheFiles()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in new[]
        {
            Path.Combine(Logger.SharedDataDir, "indexes"),
            Path.Combine(Logger.UserDataDir, "indexes")
        })
        {
            string[] paths;
            try
            {
                paths = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.idx") : Array.Empty<string>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var path in paths)
                if (seen.Add(path))
                    yield return path;
        }
    }

    public void Dispose()
    {
        foreach (var source in _sources)
            source.Dispose();
        _sources.Clear();
    }
}
