using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.SearchIndex;
namespace Lertaro.Core.Services.Search;

public static class LiveDirectorySearcher
{
    // liveQuery/onLiveMatch let the caller that actually triggers a cold (uncached) scan see matches as
    // soon as each directory is walked, instead of waiting for the whole (potentially huge) subtree to
    // finish before anything renders -- see LiveScanCache for why only the caller that wins the GetOrAdd
    // race gets this; every later caller just reuses the finished list via MatchAndStream once the shared
    // task completes. `liveQuery` is the filter's TEXT and `regexes` are the "/.../" clauses that go with
    // it, both required for the filter to mean what the user typed.
    //
    // Two tokens, because the scan and its audience have two different lifetimes. `token` governs the
    // SCAN: the scan is shared between keystrokes and outlives any one of them, so cancelling it means
    // "stop walking", which only the owning window does. `liveMatchToken` governs DELIVERY to whoever is
    // listening right now: when that request is superseded, the walk is still worth finishing for the next
    // keystroke, but its results have nowhere to go -- without this the callback captured at scan start
    // keeps firing into a caller that has already moved on. Defaults to never-cancelled, i.e. deliver for
    // as long as the scan runs, which is what a caller with no separate request token wants.
    public static List<SearchResult> ScanDirectory(
        string directory,
        int maxProcessed,
        CancellationToken token,
        string? liveQuery = null,
        Action<SearchResult>? onLiveMatch = null,
        bool onlyDirectChildren = false,
        string? parentPath = null,
        CancellationToken liveMatchToken = default,
        string[]? regexes = null)
    {
        var results = new List<SearchResult>();
        var exists = !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
        Logger.Log($"[LiveDirectorySearcher] ScanDirectory starting for '{directory}'. Exists: {exists}", LogLevel.Debug);
        if (!exists)
            return results;

        FzfPattern? livePattern = null;
        FzfSlab? liveSlab = null;
        // A regex clause is a filter on its own: the path-mode branch hands over a clause with no query
        // text at all, so the clause is the only reason to build a pattern here. Both entry points take the
        // clauses as an argument (the text handed over is already clause-free), which is why this is
        // ParseText rather than Parse -- see SearchService's live-scan setup.
        if (onLiveMatch != null && (!string.IsNullOrWhiteSpace(liveQuery) || regexes is { Length: > 0 }))
        {
            var parsed = FzfPattern.ParseText(liveQuery ?? string.Empty, regexes);
            if (!parsed.IsEmpty)
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

                if (onLiveMatch != null && !liveMatchToken.IsCancellationRequested && TryMatchEntry(result, livePattern, liveSlab, onlyDirectChildren, normalizedParent))
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
        string? parentPath = null,
        string[]? regexes = null)
    {
        if (entries == null || entries.Count == 0)
            return false;

        FzfPattern? pattern = null;
        FzfSlab? slab = null;
        // A regex clause is a filter on its own: the path-mode branch hands over a clause with no query
        // text at all, and skipping the pattern there would stream every entry the clause rejects.
        if (!string.IsNullOrWhiteSpace(query) || regexes is { Length: > 0 })
        {
            pattern = FzfPattern.ParseText(query, regexes);
            if (pattern.IsEmpty)
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

        foreach (var (alias, separator) in aliases)
        {
            // Positives read the alias, exclusions read the entry's own name -- see FzfPattern.TryMatchAlias.
            if (!pattern.TryMatchAlias(alias, entry.Name, out var aliasMatch, FzfScoringScheme.Default, slab))
                continue;
            // Same syllable-alignment rule the index scan applies (see AliasMatchRules).
            if (!AliasMatchRules.AllowsMatch(pattern, separator, alias, aliasMatch.MinBegin))
                continue;
            if (pattern.IsAcceptableAliasMatch(aliasMatch, pattern.GetTotalTermLength(), alias, FzfScoringScheme.Default, slab))
            {
                return true;
            }
        }

        return false;
    }

    // Each alias travels with its own provider's syllable separator: the alignment rule needs to know
    // where that provider's boundaries fall, and doing it here (the provider is in hand) saves looking the
    // provider up again per alias later.
    private static List<(string Alias, char Separator)>? GenerateAliases(string text)
    {
        if (string.IsNullOrEmpty(text) || AliasProviderRegistry.HasInvalidUtf16(text) || !AliasProviderRegistry.HasNonAscii(text))
            return null;

        var list = new List<(string, char)>();
        foreach (var provider in AliasProviderRegistry.GetActiveProviders())
        {
            try
            {
                var aliases = provider.GetAliases(text);
                if (aliases == null)
                    continue;

                var separator = provider.SyllableSeparator;
                foreach (var alias in aliases)
                    list.Add((alias, separator));
            }
            catch
            {
                // Ignore plugin errors
            }
        }
        return list.Count > 0 ? list : null;
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
