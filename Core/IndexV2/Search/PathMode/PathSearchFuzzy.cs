using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.IndexV2.Search.PathMode;

// Fuzzy path-mode matching as a thin layer over the shared name-mode pipeline: the file part runs
// through the SAME phase A as name search (SearchMatcherPath -- mask prefilter, byte path, pooled
// workers, parallel chunks), and path mode's only specialized piece is the per-row directory gate
// (PathGate, memoized per parent) bolted onto the fanout plus the sort-key surgery (depth|nameLen in
// bits 32-47). The filename-only branch (a path query with no directory part) is literally name mode
// fanned out per row. Delta rows (small, live-updated) keep their per-record string matching.
internal static class PathSearchFuzzy
{
    // See NameSearch's identical constants: the ranking weight is too expensive to compute inline
    // for every matched candidate, so the scan keeps a wider unweighted top-N and only that bounded
    // headroom set gets refined (filename weight * directory weight) afterward.
    private const int RefinementHeadroomFactor = 5;
    private const int RefinementScanCap = 4000;

    public static void SearchStreaming(Snapshot snapshot, DeltaOverlay delta, string pathQuery, int limit,
        Action<SearchResult> onResult, CancellationToken token, string? directoryFilterLower)
    {
        var directoryContext = NameSearch.ResolveDirectoryContext(snapshot, delta, directoryFilterLower);
        if (directoryContext.Excluded)
            return;

        // See NameSearch: bounded by the index, and widened so a large limit cannot overflow.
        var keep = (int)Math.Min((long)Math.Max(limit, 8) * 8, snapshot.Count + delta.Added.Count);
        var scanKeep = Math.Min(keep * RefinementHeadroomFactor, RefinementScanCap);
        var topN = new FzfTopN(scanKeep);

        var lastSep = pathQuery.LastIndexOf(Path.DirectorySeparatorChar);
        var dirQuery = lastSep >= 0 ? pathQuery[..lastSep] : string.Empty;
        var fileQuery = lastSep >= 0 ? pathQuery[(lastSep + 1)..] : pathQuery;

        PathGate? gate = null;
        FzfPattern? filePattern = null;
        if (!string.IsNullOrEmpty(dirQuery))
        {
            // Parse, not ParseText: a drive named in the file part ("dcj\ d01j y:") is a filter here for
            // the same reason it is one in a name-mode query. ParseText has no notion of a drive, so the
            // token stayed a plain term -- and a term containing a colon can never match a file name, so
            // one of those anywhere in the query took the whole thing down to no results at all.
            filePattern = !string.IsNullOrEmpty(fileQuery) ? FzfPattern.Parse(fileQuery) : null;
            if (filePattern?.TargetDrive != null &&
                !filePattern.TargetDrive.Equals(snapshot.SourceKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            gate = SearchWithDirectory(snapshot, delta, dirQuery, filePattern, topN, scanKeep, token, directoryContext);
        }
        else
            filePattern = SearchFilenameOnly(snapshot, delta, pathQuery, topN, token, directoryContext);

        var ranks = topN.Finish(scanKeep);
        if (filePattern is { IsEmpty: false } || gate != null)
            RefineWithWeight(snapshot, delta, gate, filePattern, ranks);

        var seen = new HashSet<int>();
        var emitted = 0;
        foreach (var rank in ranks)
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(rank.EntryIndex))
                continue;
            onResult(ResultBuilder.ToResult(snapshot, delta, rank));
            if (++emitted >= limit)
                break;
        }
    }

    // Bounded refinement over the scanKeep-sized headroom set only -- filename weight (against
    // filePattern, when there's a filename query) times directory weight (against gate, when path
    // mode has a directory part). Never rejects; FzfResultRank.ApplyWeight only adjusts sort order.
    private static void RefineWithWeight(Snapshot snapshot, DeltaOverlay delta, PathGate? gate, FzfPattern? filePattern, List<FzfRank> ranks)
    {
        var worker = gate != null ? SearchMatcher.RentWorker() : null;
        for (var i = 0; i < ranks.Count; i++)
        {
            var rank = ranks[i];
            var name = GetNameForEntry(snapshot, delta, rank.EntryIndex);
            if (name.Length == 0)
                continue;

            var weight = filePattern is { IsEmpty: false } ? HighlightMask.ComputeWeight(name, filePattern) : 1.0;
            if (gate != null)
                weight *= ComputeDirectoryWeight(snapshot, delta, gate, rank.EntryIndex, worker!);

            ranks[i] = FzfResultRank.ApplyWeight(rank, weight);
        }
        if (worker != null)
            SearchMatcher.ReturnWorker(worker);
        FzfRankRadixSorter.Sort(ranks);
    }

    // Same entryIndex->parent resolution FanoutRange (base rows, via Snapshot.ParentIndexes) and
    // MatchDeltaRowsWithDirectory (delta rows, via DeltaOverlay.GetParentPath) used during the scan --
    // recovered here post-hoc from just the entryIndex, so no side-channel needs to be threaded
    // through the bounded top-N for the small refinement set.
    private static double ComputeDirectoryWeight(Snapshot snapshot, DeltaOverlay delta, PathGate gate, int entryIndex, SearchMatcher.Worker worker)
    {
        if (entryIndex >= snapshot.Count)
            return gate.ComputeWeightForPath(delta.GetParentPath(delta.Added[entryIndex - snapshot.Count]), worker);
        if (delta.BaseOverrides.TryGetValue(entryIndex, out var overrideRecord))
            return gate.ComputeWeightForPath(delta.GetParentPath(overrideRecord), worker);

        var parentIndex = snapshot.ParentIndexes[entryIndex];
        return parentIndex < 0 ? 1.0 : gate.ComputeWeight(parentIndex, worker);
    }

    private static string GetNameForEntry(Snapshot snapshot, DeltaOverlay delta, int entryIndex)
        => entryIndex >= snapshot.Count ? delta.Added[entryIndex - snapshot.Count].Name : delta.NameOf(entryIndex);

    private static PathGate SearchWithDirectory(Snapshot snapshot, DeltaOverlay delta, string dirQuery, FzfPattern? filePattern,
        FzfTopN topN, int keep, CancellationToken token, NameSearch.DirectoryContext directoryContext)
    {
        var gate = new PathGate(snapshot, delta, dirQuery);
        var matches = SearchMatcherPath.MatchUniquesForPath(snapshot, filePattern);

        // Bounded per-worker top-N sets keep the parallel fanout's memory flat even when a broad
        // dir-only query admits most of the drive; a caller asking for an enormous keep (no real UI
        // does) falls back to the single-threaded fanout rather than multiplying that capacity.
        if (matches.Count > 1024 && keep <= 65536)
        {
            var mergeLock = new object();
            const int FanoutChunk = 1024;
            var chunkCount = (matches.Count + FanoutChunk - 1) / FanoutChunk;
            Parallel.For(
                0,
                chunkCount,
                () => (Worker: SearchMatcher.RentWorker(), TopN: new FzfTopN(keep), Membership: directoryContext.FilterLower != null ? new Dictionary<int, bool>() : null),
                (chunk, _, state) =>
                {
                    var start = chunk * FanoutChunk;
                    FanoutRange(snapshot, delta, matches, start, Math.Min(start + FanoutChunk, matches.Count), gate, state.Worker, state.TopN, token, directoryContext, state.Membership);
                    return state;
                },
                state =>
                {
                    lock (mergeLock)
                    {
                        state.TopN.DrainInto(topN);
                    }
                    SearchMatcher.ReturnWorker(state.Worker);
                });
        }
        else
        {
            var worker = SearchMatcher.RentWorker();
            var membership = directoryContext.FilterLower != null ? new Dictionary<int, bool>() : null;
            FanoutRange(snapshot, delta, matches, 0, matches.Count, gate, worker, topN, token, directoryContext, membership);
            SearchMatcher.ReturnWorker(worker);
        }

        MatchDeltaRowsWithDirectory(snapshot, delta, gate, filePattern, topN, directoryContext.FilterLower);
        return gate;
    }

    private static void FanoutRange(Snapshot snapshot, DeltaOverlay delta, List<PathUniqueMatch> matches, int from, int to,
        PathGate gate, SearchMatcher.Worker worker, FzfTopN topN, CancellationToken token, NameSearch.DirectoryContext directoryContext, Dictionary<int, bool>? membership)
    {
        var parentIndexes = snapshot.ParentIndexes;
        for (var i = from; i < to; i++)
        {
            token.ThrowIfCancellationRequested();
            var m = matches[i];
            var nameLenPoint = Math.Min(m.NameLen, 255) & 0xFF;
            foreach (var row in snapshot.RowsForUid(m.Uid))
            {
                if (snapshot.IsDeleted(row) || delta.IsSuperseded(row))
                    continue;
                var parentIndex = parentIndexes[row];
                if (parentIndex < 0)
                    continue;

                var (dirScore, depth) = gate.Verify(parentIndex, worker);
                if (dirScore <= 0)
                    continue;

                if (membership != null && !NameSearch.RowMatchesFilter(snapshot, delta, row, directoryContext, membership))
                    continue;

                var totalScore = m.Match.Score + dirScore;
                var point2 = (ushort)(ushort.MaxValue - (uint)Math.Clamp(totalScore, 0, ushort.MaxValue));
                var point3 = (ushort)((Math.Min((int)depth, 255) << 8) | nameLenPoint);
                var sortKey = m.RankLow32 | ((ulong)point3 << 32) | ((ulong)point2 << 48);
                topN.Add(new FzfRank(row, totalScore, sortKey));
            }
        }
    }

    // Delta churn is small, so it keeps a plain per-record string path -- correctness over throughput
    // for a handful of live-updated rows.
    private static void MatchDeltaRowsWithDirectory(Snapshot snapshot, DeltaOverlay delta, PathGate gate,
        FzfPattern? filePattern, FzfTopN topN, string? directoryFilterLower)
    {
        var slab = new FzfSlab();
        var worker = SearchMatcher.RentWorker();
        var fileQueryLen = filePattern?.GetTotalTermLength() ?? 0;
        foreach (var record in delta.RowsToMatch())
        {
            if (record.Name.Length == 0)
                continue;
            FzfPatternResult fileMatch = default;
            if (filePattern != null && !SearchMatcherRow.TryMatchNameOrAliases(filePattern, record.Name, record.Aliases, record.ProviderIds, fileQueryLen, slab, out fileMatch))
                continue;

            var parentPath = delta.GetParentPath(record);
            var dirScore = gate.VerifyPath(parentPath, worker);
            if (dirScore <= 0)
                continue;
            var path = delta.GetFullPath(record);
            if (directoryFilterLower != null && !path.StartsWith(directoryFilterLower, StringComparison.OrdinalIgnoreCase))
                continue;

            fileMatch = fileMatch with { Score = fileMatch.Score + dirScore };
            var entryIndex = EntryIndexOf(snapshot, delta, record);
            if (entryIndex < 0)
                continue;

            var rank = FzfResultRank.ForDefaultScheme(entryIndex, record.Name, fileMatch);
            var relativeDepth = GetRelativeDepth(path, parentPath);
            var point3 = (ushort)((Math.Min(relativeDepth, 255) << 8) | (Math.Min(record.Name.Length, 255) & 0xFF));
            var sortKey = rank.SortKey;
            sortKey &= ~(0xFFFFUL << 32);
            sortKey |= (ulong)point3 << 32;
            topN.Add(rank with { SortKey = sortKey });
        }
        SearchMatcher.ReturnWorker(worker);
    }

    private static FzfPattern SearchFilenameOnly(Snapshot snapshot, DeltaOverlay delta, string pathQuery, FzfTopN topN,
        CancellationToken token, NameSearch.DirectoryContext directoryContext)
    {
        var pattern = FzfPattern.ParseText(pathQuery);
        var membership = directoryContext.FilterLower != null ? new Dictionary<int, bool>() : null;

        var hits = SearchMatcher.RentHitList();
        SearchMatcher.MatchUniques(snapshot, pattern, hits);
        foreach (var m in hits)
        {
            token.ThrowIfCancellationRequested();
            foreach (var row in snapshot.RowsForUid(m.Uid))
            {
                if (snapshot.IsDeleted(row) || delta.IsSuperseded(row))
                    continue;
                if (membership != null && !NameSearch.RowMatchesFilter(snapshot, delta, row, directoryContext, membership))
                    continue;
                topN.Add(new FzfRank(row, m.Match.Score, m.SortKey));
            }
        }
        SearchMatcher.ReturnHitList(hits);

        var slab = new FzfSlab();
        var queryLen = pattern.GetTotalTermLength();
        foreach (var record in delta.RowsToMatch())
        {
            if (record.Name.Length == 0)
                continue;
            if (!SearchMatcherRow.TryMatchNameOrAliases(pattern, record.Name, record.Aliases, record.ProviderIds, queryLen, slab, out var match))
                continue;
            if (directoryContext.FilterLower != null && !delta.GetFullPath(record).StartsWith(directoryContext.FilterLower, StringComparison.OrdinalIgnoreCase))
                continue;
            var entryIndex = EntryIndexOf(snapshot, delta, record);
            if (entryIndex < 0)
                continue;
            topN.Add(FzfResultRank.ForDefaultScheme(entryIndex, record.Name, match));
        }
        return pattern;
    }

    private static int EntryIndexOf(Snapshot snapshot, DeltaOverlay delta, DeltaOverlay.DeltaRecord record)
    {
        if (delta.BaseOverrides.ContainsValue(record))
        {
            foreach (var (row, r) in delta.BaseOverrides)
                if (ReferenceEquals(r, record))
                    return row;
            return -1;
        }
        var added = delta.Added.IndexOf(record);
        return added < 0 ? -1 : snapshot.Count + added;
    }

    private static int GetRelativeDepth(string path, string basePath)
    {
        var count = 0;
        for (var i = basePath.Length; i < path.Length; i++)
            if (path[i] == '\\' || path[i] == '/')
                count++;
        return count;
    }
}
