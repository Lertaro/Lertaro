using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Query;
namespace Lertaro.Core.Indexer.NetworkDrive;

public static class NetworkIndexerSearchExtensions
{

    public static void SearchStreaming(
        this NetworkIndexer indexer,
        string query,
        int limit,
        Action<SearchResult> onResult,
        CancellationToken token = default,
        string? directoryFilter = null,
        string? fileNameFilter = null)
    {
        indexer.EnsureConfigured();
        if (limit <= 0 || string.IsNullOrWhiteSpace(query))
            return;

        NetworkIndex[] snapshots;
        lock (indexer.Gate)
            snapshots = indexer._indexes.Values.ToArray();

        if (snapshots.Length == 0)
            return;

        var parsed = SearchQueryParser.Parse(query);
        var directoryFilterLower = IndexerHelper.NormalizeFilter(directoryFilter);
        SearchSnapshots(snapshots, parsed, query, directoryFilterLower, limit, onResult, token, fileNameFilter);
    }

    internal static void SearchSnapshots(
        NetworkIndex[] snapshots,
        SearchQueryParser.ParsedQuery parsed,
        string query,
        string? directoryFilterLower,
        int limit,
        Action<SearchResult> onResult,
        CancellationToken token,
        string? fileNameFilter = null)
    {
        var allowed = snapshots.Where(index => IsDriveAllowed(index.Drive, parsed, directoryFilterLower)).ToArray();
        if (allowed.Length == 0 || limit <= 0)
            return;

        if (allowed.Length == 1)
        {
            allowed[0].SearchStreaming(parsed, query, directoryFilterLower, limit, onResult, token, fileNameFilter);
            return;
        }

        // Bounded queries cut rows across shares, so merge each share's best `limit` by SearchCandidateComparer
        // instead of letting whichever share answers first exhaust the shared budget.
        if (limit < int.MaxValue)
        {
            var buffered = new List<SearchResult>[allowed.Length];
            Parallel.For(0, allowed.Length, new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = 2
            }, i =>
            {
                var rows = buffered[i] = new List<SearchResult>();
                try
                {
                    allowed[i].SearchStreaming(parsed, query, directoryFilterLower, limit, rows.Add, token, fileNameFilter);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.Log($"[NetworkIndexer] Search of '{allowed[i].Drive}' failed; its rows are left out: {ex.Message}", LogLevel.Warn);
                }
            });

            foreach (var row in buffered.SelectMany(rows => rows).OrderBy(row => row, SearchCandidateComparer.Instance).Take(limit))
            {
                token.ThrowIfCancellationRequested();
                onResult(row);
            }
            return;
        }

        var callbackLock = new object();
        Parallel.ForEach(
            allowed,
            new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = 2
            },
            index =>
            {
                token.ThrowIfCancellationRequested();
                index.SearchStreaming(parsed, query, directoryFilterLower, limit, result =>
                {
                    lock (callbackLock)
                        onResult(result);
                }, token, fileNameFilter);
            });
    }

    // No fan-out and no IsDriveAllowed pre-filter, unlike the search above: a path lives under one
    // index root, and each index rejects a path outside its own source root before doing any work.
    // First one that holds it answers -- with nested roots (a folder index inside a mapped share) both
    // hold the same content, so either answer is the same content.
    public static bool EnumerateDirectory(
        this NetworkIndexer indexer,
        string path,
        bool recursive,
        string[]? patterns,
        int limit,
        Action<SearchResult> onResult,
        CancellationToken token)
    {
        indexer.EnsureConfigured();

        NetworkIndex[] snapshots;
        lock (indexer.Gate)
            snapshots = indexer._indexes.Values.ToArray();

        foreach (var index in snapshots)
        {
            token.ThrowIfCancellationRequested();
            if (index.EnumerateDirectory(path, recursive, patterns, limit, onResult, token))
                return true;
        }
        return false;
    }

    private static bool IsDriveAllowed(string indexDrive, ParsedSearchQuery parsed, string? directoryFilterLower)
    {
        // The "d:foo" query-scoping modifier only makes sense against a bare drive letter -- a
        // folder-index or UNC key (anything longer) can never equal it, so it's always excluded once
        // a target drive is requested.
        if (parsed.TargetDrive != null && !(indexDrive.Length == 1 && parsed.TargetDrive.Equals(indexDrive, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (directoryFilterLower == null)
            return true;

        if (indexDrive.Length == 1)
            return directoryFilterLower.StartsWith(indexDrive + @":\", StringComparison.OrdinalIgnoreCase);

        // A folder-index or UNC key is already a full rooted path -- allow either direction: the
        // filter scope nested under the index root, or the index root nested under a broader filter
        // scope (e.g. a folder index for D:\Projects\ProjectA should still be searched when the
        // filter scope is the parent D:\Projects\). Compare with a trailing separator on both sides
        // so "D:\Foo" doesn't also match a filter under a sibling "D:\FooBar".
        var rootedDrive = indexDrive.EndsWith(Path.DirectorySeparatorChar) ? indexDrive : indexDrive + Path.DirectorySeparatorChar;
        var rootedFilter = directoryFilterLower.EndsWith(Path.DirectorySeparatorChar) ? directoryFilterLower : directoryFilterLower + Path.DirectorySeparatorChar;
        return rootedFilter.StartsWith(rootedDrive, StringComparison.OrdinalIgnoreCase) || rootedDrive.StartsWith(rootedFilter, StringComparison.OrdinalIgnoreCase);
    }
}
