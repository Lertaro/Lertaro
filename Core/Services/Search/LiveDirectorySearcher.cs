using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.SearchIndex;
namespace Lertaro.Core.Services.Search;

public static class LiveDirectorySearcher
{
    // liveQuery/onLiveMatch let the caller that actually triggers a cold (uncached) scan see matches as
    // soon as each directory is walked, instead of waiting for the whole (potentially huge) subtree to
    // finish before anything renders -- see SearchService's _sessionDirectoryCache for why only the
    // caller that wins the GetOrAdd race gets this; every later caller just reuses the finished list via
    // MatchAndStream once the shared task completes.
    public static List<SearchResult> ScanDirectory(
        string directory,
        int maxProcessed,
        CancellationToken token,
        string? liveQuery = null,
        Action<SearchResult>? onLiveMatch = null,
        bool onlyDirectChildren = false,
        string? parentPath = null)
    {
        var results = new List<SearchResult>();
        var exists = !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
        Logger.Log($"[LiveDirectorySearcher] ScanDirectory starting for '{directory}'. Exists: {exists}", LogLevel.Debug);
        if (!exists)
            return results;

        FzfPattern? livePattern = null;
        FzfSlab? liveSlab = null;
        if (onLiveMatch != null && !string.IsNullOrWhiteSpace(liveQuery))
        {
            var parsed = FzfPattern.Parse(liveQuery);
            if (!parsed.IsEmpty || parsed.TargetDrive != null)
            {
                livePattern = parsed;
                liveSlab = new FzfSlab();
            }
        }
        var normalizedParent = onlyDirectChildren && !string.IsNullOrEmpty(parentPath)
            ? parentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : null;

        var queue = new Queue<string>();
        queue.Enqueue(directory);

        var drive = Path.GetPathRoot(directory) ?? string.Empty;
        var processedCount = 0;

        while (queue.Count > 0 && processedCount < maxProcessed)
        {
            token.ThrowIfCancellationRequested();
            var currentDir = queue.Dequeue();

            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(currentDir).GetFileSystemInfos();
            }
            catch
            {
                continue;
            }

            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attrs;
                try { attrs = entry.Attributes; } catch { continue; }
                processedCount++;
                if (processedCount >= maxProcessed)
                    break;

                var isDir = attrs.HasFlag(FileAttributes.Directory);
                var result = new SearchResult
                {
                    Name = entry.Name,
                    Path = entry.FullName,
                    IsDir = isDir,
                    Drive = drive,
                    Attributes = attrs
                };
                results.Add(result);

                if (isDir)
                {
                    queue.Enqueue(entry.FullName);
                }

                if (onLiveMatch != null && TryMatchEntry(result, livePattern, liveSlab, onlyDirectChildren, normalizedParent))
                    onLiveMatch(result);
            }
        }
        Logger.Log($"[LiveDirectorySearcher] ScanDirectory finished for '{directory}'. Found: {results.Count}", LogLevel.Debug);
        return results;
    }

    public static bool MatchAndStream(
        List<SearchResult> entries,
        string query,
        Action<SearchResult> onResult,
        CancellationToken token,
        bool onlyDirectChildren = false,
        string? parentPath = null)
    {
        if (entries == null || entries.Count == 0)
            return false;

        FzfPattern? pattern = null;
        FzfSlab? slab = null;
        if (!string.IsNullOrWhiteSpace(query))
        {
            pattern = FzfPattern.Parse(query);
            if (pattern.IsEmpty && pattern.TargetDrive == null)
                return false;
            slab = new FzfSlab();
        }

        var foundCount = 0;
        var normalizedParent = onlyDirectChildren && !string.IsNullOrEmpty(parentPath)
            ? parentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : null;

        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();

            if (TryMatchEntry(entry, pattern, slab, onlyDirectChildren, normalizedParent))
            {
                onResult(entry);
                foundCount++;
            }
        }

        return foundCount > 0;
    }

    private static bool TryMatchEntry(
        SearchResult entry,
        FzfPattern? pattern,
        FzfSlab? slab,
        bool onlyDirectChildren,
        string? normalizedParent)
    {
        if (onlyDirectChildren && normalizedParent != null)
        {
            var entryParent = Path.GetDirectoryName(entry.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                ?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(entryParent, normalizedParent, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (pattern == null)
            return true;

        // 1. Try match the name itself using the core FZF engine
        if (pattern.TryMatch(entry.Name, out _, FzfScoringScheme.Default, slab))
            return true;

        // 2. Try match aliases generated for the name
        var aliases = GenerateAliases(entry.Name);
        if (aliases == null)
            return false;

        foreach (var alias in aliases)
        {
            if (pattern.TryMatch(alias, out var aliasMatch, FzfScoringScheme.Default, slab)
                && pattern.IsAcceptableAliasMatch(aliasMatch, pattern.GetTotalTermLength(), alias, FzfScoringScheme.Default, slab))
            {
                return true;
            }
        }

        return false;
    }

    private static string[]? GenerateAliases(string text)
    {
        if (string.IsNullOrEmpty(text) || AliasProviderRegistry.HasInvalidUtf16(text) || !AliasProviderRegistry.HasNonAscii(text))
            return null;

        var list = new List<string>();
        foreach (var provider in AliasProviderRegistry.GetActiveProviders())
        {
            try
            {
                var aliases = provider.GetAliases(text);
                if (aliases != null)
                {
                    list.AddRange(aliases);
                }
            }
            catch
            {
                // Ignore plugin errors
            }
        }
        return list.Count > 0 ? list.ToArray() : null;
    }

    public static (string DirectoryToScan, string FilterQuery) ResolvePathModeSearch(string exactPathLower)
    {
        if (string.IsNullOrEmpty(exactPathLower) || WslPath.IsPath(exactPathLower))
            return (string.Empty, string.Empty);

        if (Directory.Exists(exactPathLower))
        {
            return (exactPathLower, string.Empty);
        }

        var dir = Path.GetDirectoryName(exactPathLower);
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(dir))
            {
                var filter = exactPathLower.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return (dir, filter);
            }
            dir = Path.GetDirectoryName(dir);
        }

        return (string.Empty, string.Empty);
    }
}
