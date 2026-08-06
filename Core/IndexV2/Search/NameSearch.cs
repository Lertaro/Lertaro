using System.Runtime.InteropServices;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.IndexV2.Search;

// Name-mode search: phase A matches unique names (SearchMatcher) + delta rows (renamed/added, matched
// individually since they aren't folded into the unique table until compaction); phase B fans each
// matched unique out through the uid->rows CSR and ranks everything with FzfTopN. Delta rows rank
// under their original row index (base overrides -- the old engine renames in place) or a synthetic
// index past Count in insertion order (added rows) -- FzfRank breaks ties by EntryIndex, so relative
// order stays equivalent to the old engine's append-and-rename-in-place behavior.
internal static class NameSearch
{
    private static readonly FzfPatternResult EmptyPatternMatch = new(0, int.MaxValue, int.MaxValue, 0, false);

    // The scan keeps a WIDER unweighted top-N than what gets displayed, and only that headroom set gets
    // refined with the real percentage*consecutiveness weight (HighlightMask.ComputeWeight) afterward.
    // The weight is far too expensive to compute per candidate inside the hot scan -- it is dominated by
    // the DP fuzzy-highlight fallback for scattered matches -- so it is only ever paid for candidates
    // that could plausibly be shown.
    private const int RefinementHeadroomFactor = 5;

    public static void SearchStreaming(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, int limit,
        Action<SearchResult> onResult, CancellationToken token, string? directoryFilterLower)
    {
        if (!DriveAdmits(snapshot, pattern, out var matchAll))
            return;

        var directoryContext = ResolveDirectoryContext(snapshot, delta, directoryFilterLower);
        if (directoryContext.Excluded)
            return;

        // Nothing is truncated on the way through: the only ceiling is the index itself, since a search
        // cannot return more rows than exist. That bound matters for more than tidiness -- FzfTopN
        // pre-allocates twice its capacity, so deriving it from the caller's limit (which is now
        // effectively unbounded) would reserve arrays for a number of results nobody can reach.
        //
        // The arithmetic is widened deliberately. keep * RefinementHeadroomFactor overflows int once the
        // limit passes about 53 million, and a negative capacity used to reach FzfTopN.Finish and throw
        // from RemoveRange -- unreachable while the limit was clamped to 2000, immediate once it is not.
        var everything = snapshot.Count + delta.Added.Count;
        var keep = (int)Math.Min((long)Math.Max(limit, 8) * 8, everything);
        var scanKeep = matchAll || pattern.IsEmpty
            ? keep
            : (int)Math.Min((long)keep * RefinementHeadroomFactor, everything);
        var topN = new FzfTopN(Math.Max(scanKeep, 1));
        CollectRanks(snapshot, delta, pattern, matchAll, directoryContext, topN, token);

        var ranks = topN.Finish(scanKeep);
        if (!matchAll && !pattern.IsEmpty)
            RefineWithWeight(snapshot, delta, pattern, ranks);

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

        // The names alone did not fill the page: top it up with rows where a term is satisfied by an
        // ancestor folder instead. Gated on there being room left, so a query that already answers in
        // full never pays for it -- and since these are appended after every name hit, a real name
        // match still cannot be pushed down by a weaker path-derived one.
        //
        // Gating on emitted == 0 instead was too strict to be useful: one incidental name hit
        // suppressed the entire path pass, so a query whose initials matched both a file and a folder
        // returned only the file and hid every result under the folder.
        if (emitted < limit)
            PathTermFallback.SearchStreaming(snapshot, delta, pattern, limit - emitted, onResult, token, directoryFilterLower);
    }

    [ThreadStatic]
    private static Dictionary<string, double>? _weightsByName;

    // Bounded refinement: only ever runs over the scanKeep-sized headroom set above, never the full
    // matched set. Ranking-only (FzfResultRank.ApplyWeight never rejects), so this can't drop a result.
    private static void RefineWithWeight(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, List<FzfRank> ranks)
    {
        // The weight depends on the name and the pattern, and on nothing else about the row -- so every
        // row sharing a name shares its weight, and a set of a few thousand holds only about two thirds
        // that many distinct names. Worth remembering because the calculation is not cheap for a query
        // that matches through an alias: it has to ask the provider for the candidate's own spellings.
        // Reused per thread rather than built per search: a query whose names all match literally gets
        // no benefit from the memo and should not pay to allocate one either.
        var weights = _weightsByName ??= new Dictionary<string, double>(StringComparer.Ordinal);
        weights.Clear();
        for (var i = 0; i < ranks.Count; i++)
        {
            var rank = ranks[i];
            var name = GetNameForEntry(snapshot, delta, rank.EntryIndex);
            if (name.Length == 0)
                continue;
            ref var weight = ref CollectionsMarshal.GetValueRefOrAddDefault(weights, name, out var known);
            if (!known)
                weight = HighlightMask.ComputeWeight(name, pattern);
            ranks[i] = FzfResultRank.ApplyWeight(rank, weight);
        }
        FzfRankRadixSorter.Sort(ranks);

        // Emptied here rather than only on the way in. Clearing on entry leaves the buckets from the last
        // search sitting in a thread static for as long as the process is idle, and a whole-drive query
        // sizes them to its own name count -- see SearchScratchPolicy.
        SearchScratchPolicy.ClearAndTrim(weights);
    }

    // Mirrors ResultBuilder.ToResult's entryIndex->name resolution (base row, possibly overridden, vs
    // an Added delta record past Snapshot.Count).
    private static string GetNameForEntry(Snapshot snapshot, DeltaOverlay delta, int entryIndex)
        => entryIndex >= snapshot.Count ? delta.Added[entryIndex - snapshot.Count].Name : delta.NameOf(entryIndex);

    // Mirrors Searcher's drive gate: a foreign-drive query returns nothing; a bare drive prefix with
    // no terms ("t:") matches everything (TryMatch trivially succeeds on an empty pattern).
    private static bool DriveAdmits(Snapshot snapshot, FzfPattern pattern, out bool matchAll)
    {
        matchAll = false;
        if (pattern.TargetDrive != null && !pattern.TargetDrive.Equals(snapshot.SourceKey, StringComparison.OrdinalIgnoreCase))
            return false;
        if (pattern.IsEmpty)
        {
            if (pattern.TargetDrive == null)
                return false;
            matchAll = true;
        }
        return true;
    }

    internal readonly record struct DirectoryContext(bool Excluded, int RootFilterRow, int AncestorRow, string? FilterLower);

    internal static DirectoryContext ResolveDirectoryContext(Snapshot snapshot, DeltaOverlay? delta, string? directoryFilterLower)
    {
        var sourceRootLower = snapshot.SourceRoot.ToLowerInvariant();
        if (directoryFilterLower != null && directoryFilterLower.Equals(sourceRootLower, StringComparison.Ordinal))
            directoryFilterLower = null;
        if (DirectoryFilterResolver.ExcludesSource(snapshot, directoryFilterLower))
            return new DirectoryContext(true, -1, -1, directoryFilterLower);
        if (directoryFilterLower == null)
            return new DirectoryContext(false, -1, -1, null);

        var rootFilterRow = -1;
        var ancestorRow = -1;
        if (DirectoryFilterResolver.TryResolve(snapshot, delta, directoryFilterLower, forceLastSegmentAsQuery: false, out var resolved, out var remainder))
        {
            if (remainder.Length == 0)
                rootFilterRow = resolved;
            else
                ancestorRow = resolved;
        }
        return new DirectoryContext(false, rootFilterRow, ancestorRow, directoryFilterLower);
    }

    // True when `row` (a base-snapshot row) satisfies the resolved directory filter.
    internal static bool RowMatchesFilter(Snapshot snapshot, DeltaOverlay? delta, int row, string path, DirectoryContext ctx, Dictionary<int, bool> membership)
    {
        if (ctx.FilterLower == null)
            return true;
        if (ctx.RootFilterRow >= 0)
            return DirectoryFilterResolver.IsUnderCached(snapshot, row, ctx.RootFilterRow, membership);
        if (ctx.AncestorRow >= 0 && !DirectoryFilterResolver.IsUnderCached(snapshot, row, ctx.AncestorRow, membership))
            return false;
        return path.StartsWith(ctx.FilterLower, StringComparison.OrdinalIgnoreCase);
    }

    private static void CollectRanks(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, bool matchAll, DirectoryContext ctx, FzfTopN topN, CancellationToken token)
    {
        var membership = ctx.FilterLower != null ? new Dictionary<int, bool>() : null;

        // Hoisted out of the per-row loops below. Snapshot.Flags builds a span over mapped memory on
        // every access and IsSuperseded is three hash lookups, both of which the fanout was paying for
        // every one of the tens of thousands of rows a broad query reaches.
        var flags = snapshot.Flags;
        const ushort deletedFlag = (ushort)FileRecordFlags.Deleted;
        var mayBeSuperseded = !delta.HasNoBaseChanges;

        if (matchAll)
        {
            // Unique-first like the pattern path below: the empty-pattern sort key depends only on the
            // name, so it's computed once per unique instead of materializing a string per row.
            // Superseded rows are skipped in the fanout, so no override name can be needed here.
            var worker = SearchMatcher.RentWorker();
            for (var uid = 0; uid < snapshot.UniqueCount; uid++)
            {
                if ((uid & 0xFFF) == 0)
                    token.ThrowIfCancellationRequested();
                var utf8 = snapshot.UniqueNameUtf8(uid);
                if (utf8.Length == 0)
                    continue;
                var sortKey = MatchAllSortKey(snapshot, uid, worker, utf8);
                foreach (var row in snapshot.RowsForUid(uid))
                {
                    if ((flags[row] & deletedFlag) != 0 || (mayBeSuperseded && delta.IsSuperseded(row)))
                        continue;
                    if (membership != null && !RowMatchesFilter(snapshot, delta, row, delta.GetFullPath(row), ctx, membership))
                        continue;
                    topN.Add(new FzfRank(row, 0, sortKey));
                }
            }
            SearchMatcher.ReturnWorker(worker);
        }
        else
        {
            var hits = SearchMatcher.RentHitList();
            SearchMatcher.MatchUniques(snapshot, pattern, hits, token);
            foreach (var m in hits)
            {
                foreach (var row in snapshot.RowsForUid(m.Uid))
                {
                    if ((flags[row] & deletedFlag) != 0 || (mayBeSuperseded && delta.IsSuperseded(row)))
                        continue;
                    if (membership != null && !RowMatchesFilter(snapshot, delta, row, delta.GetFullPath(row), ctx, membership))
                        continue;
                    // The per-unique sort key applies verbatim to every row of that unique --
                    // EntryIndex isn't packed into the key, so nothing is recomputed per row.
                    topN.Add(new FzfRank(row, m.Match.Score, m.SortKey));
                }
            }
            SearchMatcher.ReturnHitList(hits);
        }

        MatchDeltaRows(snapshot, delta, pattern, matchAll, ctx, topN);
    }

    private static ulong MatchAllSortKey(Snapshot snapshot, int uid, SearchMatcher.Worker worker, ReadOnlySpan<byte> utf8)
    {
        if (snapshot.IsUniqueAscii(uid))
            return FzfBytePattern.ForDefaultScheme(0, utf8, EmptyPatternMatch).SortKey;
        if (worker.Scratch.Length < utf8.Length)
            worker.Scratch = new char[Math.Max(utf8.Length, worker.Scratch.Length * 2)];
        var written = System.Text.Encoding.UTF8.GetChars(utf8, worker.Scratch);
        return FzfResultRank.ForDefaultScheme(0, worker.Scratch.AsSpan(0, written), EmptyPatternMatch).SortKey;
    }

    // Delta churn is always small (live USN/watcher batches, not bulk scans), so both loops just check
    // the row's own full path against the filter prefix -- correct for renamed/moved/added rows alike,
    // unlike the row-index ancestor cache above (a snapshot-only optimization for the hot base-row path).
    private static void MatchDeltaRows(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, bool matchAll, DirectoryContext ctx, FzfTopN topN)
    {
        var slab = new FzfSlab();
        var queryLen = pattern.GetTotalTermLength();

        foreach (var (row, record) in delta.BaseOverrides)
        {
            if (record.Name.Length == 0)
                continue;
            var match = EmptyPatternMatch;
            if (!matchAll && !SearchMatcherRow.TryMatchNameOrAliases(pattern, record.Name, record.Aliases, record.ProviderIds, queryLen, slab, out match))
                continue;
            if (ctx.FilterLower != null && !delta.GetFullPath(row).StartsWith(ctx.FilterLower, StringComparison.OrdinalIgnoreCase))
                continue;
            topN.Add(FzfResultRank.ForDefaultScheme(row, record.Name, match));
        }
        for (var i = 0; i < delta.Added.Count; i++)
        {
            var record = delta.Added[i];
            if (record.Removed || record.Name.Length == 0)
                continue;
            var match = EmptyPatternMatch;
            if (!matchAll && !SearchMatcherRow.TryMatchNameOrAliases(pattern, record.Name, record.Aliases, record.ProviderIds, queryLen, slab, out match))
                continue;
            if (ctx.FilterLower != null && !delta.GetFullPath(record).StartsWith(ctx.FilterLower, StringComparison.OrdinalIgnoreCase))
                continue;
            topN.Add(FzfResultRank.ForDefaultScheme(snapshot.Count + i, record.Name, match));
        }
    }
}
