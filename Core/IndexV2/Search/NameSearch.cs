using System.Runtime.InteropServices;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.Core.IndexV2.Delta;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.Services.Plugin.DirectoryIndex;
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
        Action<SearchResult> onResult, CancellationToken token, string? directoryFilterLower, string[]? fileNamePatterns = null)
    {
        if (!DriveAdmits(snapshot, pattern, out var matchAll))
            return;

        var directoryContext = ResolveDirectoryContext(snapshot, delta, directoryFilterLower);
        if (directoryContext.Excluded)
            return;

        // Nothing is truncated on the way through: the only ceiling is the index itself, since a search
        // cannot return more rows than exist. That bound matters for more than tidiness -- FzfTopN's
        // buffer ceiling is twice its capacity, so deriving it from the caller's limit (which is now
        // effectively unbounded) would let that ceiling grow to a number of results nobody can reach.
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
        // CollectRanks hands its rented hit list back instead of recycling it, because the fallback below
        // needs to know which uids the WHOLE query already matched (see BuildReportedUids); the finally is
        // what keeps that from leaking the list whenever a superseded search cancels out of here.
        var hits = CollectRanks(snapshot, delta, pattern, matchAll, directoryContext, topN, token, fileNamePatterns);
        try
        {
            PublishRanks(snapshot, delta, pattern, hits, matchAll, topN, scanKeep, limit, directoryFilterLower, token, onResult);
        }
        finally
        {
            if (hits != null)
                SearchMatcher.ReturnHitList(hits);
        }
    }

    private static void PublishRanks(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, List<UniqueMatch>? hits, bool matchAll,
        FzfTopN topN, int scanKeep, int limit, string? directoryFilterLower, CancellationToken token, Action<SearchResult> onResult)
    {
        var ranks = topN.Finish(scanKeep);
        if (!matchAll && !pattern.IsEmpty)
            RefineRanks(snapshot, delta, pattern, ranks, limit);

        var seen = new HashSet<int>();
        var emitted = 0;
        foreach (var rank in ranks)
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(rank.EntryIndex))
                continue;
            var result = ResultBuilder.ToResult(snapshot, delta, rank);
            SearchResultRelevance.Apply(result, pattern);
            onResult(result);
            if (++emitted >= limit)
                break;
        }

        // The names alone did not fill the page: top it up with rows where a term is satisfied by an
        // ancestor folder instead. Gated on there being room left, so a query that already answers in
        // full never pays for it. Results still stream in phase order, but both paths receive the same
        // whole-path RankSortKey above, so every consumer that merges streamed results compares them on
        // relevance rather than on the order in which a phase happened to complete.
        //
        // Gating on emitted == 0 instead was too strict to be useful: one incidental name hit
        // suppressed the entire path pass, so a query whose initials matched both a file and a folder
        // returned only the file and hid every result under the folder.
        //
        // ponytail: for the full window this gate never closes -- its limit is int.MaxValue by design
        // (SearchViewModel.FullSearchFileLimit), so "room left" is always true and a multi-term query
        // pays one extra whole-index scan of the unique-name table per term. Left as it is on purpose
        // (decided 2026-09-22): whatever the full window finds is what it returns, so the only ways to
        // close the gate are a first-page ceiling or narrowing what the window may answer -- both are
        // product/relevance decisions, not a fix that belongs at this line. The quick window's 51 and
        // the inline window's bounded limit do close it.
        if (emitted < limit)
        {
            var reportedUids = BuildReportedUids(pattern, hits);
            PathTermFallback.SearchStreaming(snapshot, delta, pattern, limit - emitted, result =>
            {
                SearchResultRelevance.Apply(result, pattern);
                onResult(result);
            }, token, directoryFilterLower, reportedUids);
        }
    }

    // The uids phase A matched, i.e. those whose WHOLE query landed on one spelling of the name -- what
    // PathTermFallback must not report a second time. It cannot be read off that pass's own per-term masks:
    // a name whose terms are satisfied by DIFFERENT spellings fills the same masks without ever having
    // matched the whole query, and those rows are the ones the pass exists to add.
    //
    // Built only when the fallback is about to run: a page that filled means the name search answered in full
    // and the fallback is skipped, so this stays bounded by the rows that did not fill the page (plus any whose
    // rows the directory/file filters dropped). Null for the shapes that pass does not handle per-term
    // (OR-first branch queries, whose recursive call keeps the old "assume it was reported" rule).
    private static HashSet<int>? BuildReportedUids(FzfPattern pattern, List<UniqueMatch>? hits)
    {
        if (hits == null || pattern.OrGroups != null
            || pattern.TermSets.Length < 2 || pattern.TermSets.Length > PathTermFallback.MaxTerms)
            return null;

        var uids = new HashSet<int>(hits.Count);
        foreach (var hit in hits)
            uids.Add(hit.Uid);
        return uids;
    }

    [ThreadStatic]
    private static Dictionary<string, MatchRank>? _ranksByName;

    // Bounded refinement: only ever runs over the scanKeep-sized headroom set above, never the full
    // matched set. Ranking-only (never rejects), so this can't drop a result.
    //
    // Rewrites each candidate's high 32 bits to the SAME relevance key SearchResultRelevance.Apply gives
    // the emitted result, so the `limit` cut in SearchStreaming drops the rows every window would rank
    // last. Cutting on the raw fzf score instead let a whole-word hit later in a name ("code-atlas") lose
    // its slot to rows the windows then sorted below it.
    //
    // ponytail: the headroom set itself is still pre-selected by raw fzf score (scanKeep = limit * 40).
    // Ceiling: more than scanKeep rows with a higher raw score can still crowd out a whole-word hit before
    // it gets here. Upgrade path: classify MatchShape inside the hot scan and let FzfTopN keep by it.
    //
    // Recency and depth need the row's times and path, so they are only worked out when the limit can
    // actually cut (the quick and inline windows). The full window keeps everything, Apply gives each
    // emitted row its complete key, and building every path twice would only slow its broadest queries.
    private static void RefineRanks(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, List<FzfRank> ranks, int limit)
    {
        var cuts = ranks.Count > limit;
        var nowUnix = FileTimeHelper.ToUnixSeconds(DateTime.UtcNow);
        var depthByParent = cuts ? new Dictionary<int, int>() : null;
        // The rank depends on the name and the pattern, and on nothing else about the row -- so every
        // row sharing a name shares it, and a set of a few thousand holds only about two thirds that
        // many distinct names. Worth remembering because the calculation is not cheap for a query that
        // matches through an alias: it has to ask the provider for the candidate's own spellings.
        // Reused per thread rather than built per search: a query whose names all match literally gets
        // no benefit from the memo and should not pay to allocate one either.
        var ranksByName = _ranksByName ??= new Dictionary<string, MatchRank>(StringComparer.Ordinal);
        ranksByName.Clear();
        for (var i = 0; i < ranks.Count; i++)
        {
            var rank = ranks[i];
            var name = GetNameForEntry(snapshot, delta, rank.EntryIndex);
            if (name.Length == 0)
                continue;
            ref var match = ref CollectionsMarshal.GetValueRefOrAddDefault(ranksByName, name, out var known);
            if (!known)
                match = HighlightMask.ComputeRank(name, pattern);
            var (band, depth) = cuts
                ? RowSignals(snapshot, delta, rank.EntryIndex, nowUnix, depthByParent!)
                : (RankSignals.BandOlder, 0);
            var high = SearchResultRelevance.HighKey(match, IsEntryDirectory(snapshot, delta, rank.EntryIndex), band, depth);
            ranks[i] = rank with { SortKey = ((ulong)high << 32) | (rank.SortKey & 0xFFFF_FFFFUL) };
        }
        FzfRankRadixSorter.Sort(ranks);

        // Emptied here rather than only on the way in. Clearing on entry leaves the buckets from the last
        // search sitting in a thread static for as long as the process is idle, and a whole-drive query
        // sizes them to its own name count -- see SearchScratchPolicy.
        SearchScratchPolicy.ClearAndTrim(ranksByName);
    }

    // The same recency band and depth Apply derives from the built result. Depth is worked out once per
    // parent folder, since a headroom set is mostly siblings; a row with no resolved parent (an orphan,
    // or an added delta row) pays for its own path.
    //
    // ponytail: depth costs one built path per distinct parent in the headroom set (measured 30 to ~5100
    // per bounded search on a 20k-name drive, together with the band work about 2 ms of a ~22 ms search).
    // Counting the parent chain instead is NOT the same thing: DeltaPathBuilder forwards a renamed-away row
    // to the FRN's live identity, which can sit at a different depth, so the count would disagree with the
    // key Apply writes on the emitted result. Upgrade path: count only while no ancestor is renamed away,
    // and fall back to the built path for the rest.
    private static (int Band, int Depth) RowSignals(Snapshot snapshot, DeltaOverlay delta, int entryIndex, uint nowUnix, Dictionary<int, int> depthByParent)
    {
        if (entryIndex >= snapshot.Count)
        {
            var record = delta.Added[entryIndex - snapshot.Count];
            return (RankSignals.RecencyBand(record.Creation, record.LastWrite, nowUnix), RankSignals.PathDepth(delta.GetFullPath(record)));
        }

        var (_, creation, lastWrite, _) = delta.MetadataOf(entryIndex);
        var band = RankSignals.RecencyBand(creation, lastWrite, nowUnix);
        var parent = delta.ParentOf(entryIndex);
        if (parent < 0 || parent == entryIndex)
            return (band, RankSignals.PathDepth(delta.GetFullPath(entryIndex)));

        ref var parentDepth = ref CollectionsMarshal.GetValueRefOrAddDefault(depthByParent, parent, out var known);
        if (!known)
            parentDepth = RankSignals.PathDepth(delta.GetFullPath(parent));
        return (band, parentDepth + 1);
    }

    // Mirrors ResultBuilder.ToResult's entryIndex->name resolution (base row, possibly overridden, vs
    // an Added delta record past Snapshot.Count).
    private static string GetNameForEntry(Snapshot snapshot, DeltaOverlay delta, int entryIndex)
        => entryIndex >= snapshot.Count ? delta.Added[entryIndex - snapshot.Count].Name : delta.NameOf(entryIndex);

    private static bool IsEntryDirectory(Snapshot snapshot, DeltaOverlay delta, int entryIndex)
        => entryIndex >= snapshot.Count
            ? (delta.Added[entryIndex - snapshot.Count].Flags & (ushort)FileRecordFlags.Directory) != 0
            : IsDirectory(snapshot, delta, entryIndex);

    // Drive gate: a foreign-drive query returns nothing; a bare drive prefix with
    // no terms ("t:") matches everything -- via the matchAll set here, NOT because TryMatch accepts an
    // empty pattern, since FzfPatternMatcher refuses a pattern with no positive term.
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
            // IsUnderCached only accepts snapshot rows. Keep an added directory as a path-prefix
            // filter so its live path still scopes both base and delta rows without indexing a
            // synthetic entry into the snapshot-only ancestor cache.
            if (remainder.Length == 0 && resolved < snapshot.Count)
                rootFilterRow = resolved;
            else if (resolved < snapshot.Count)
                ancestorRow = resolved;
        }
        return new DirectoryContext(false, rootFilterRow, ancestorRow, directoryFilterLower);
    }

    // True when `row` (a base-snapshot row) satisfies the resolved directory filter.
    internal static bool RowMatchesFilter(Snapshot snapshot, DeltaOverlay delta, int row, DirectoryContext ctx, Dictionary<int, bool> membership)
    {
        if (ctx.FilterLower == null)
            return true;
        if (ctx.RootFilterRow >= 0)
            return DirectoryFilterResolver.IsUnderCached(snapshot, row, ctx.RootFilterRow, membership);
        if (ctx.AncestorRow >= 0 && !DirectoryFilterResolver.IsUnderCached(snapshot, row, ctx.AncestorRow, membership))
            return false;
        return delta.GetFullPath(row).StartsWith(ctx.FilterLower, StringComparison.OrdinalIgnoreCase);
    }

    // Fills topN with every ranked candidate and hands back the rented hit list phase A matched on (null
    // when there is no pattern to match, i.e. a bare-drive listing). The caller owns that list and returns
    // it to the pool -- see the try/finally in SearchStreaming, which needs it to tell PathTermFallback
    // which uids the whole query already matched.
    private static List<UniqueMatch>? CollectRanks(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, bool matchAll, DirectoryContext ctx, FzfTopN topN, CancellationToken token, string[]? fileNamePatterns)
    {
        List<UniqueMatch>? rentHits = null;
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
                if (fileNamePatterns != null
                    && !FilterPatternHelper.Matches(snapshot.GetUniqueName(uid), fileNamePatterns)
                    && !SearchMatcher.HasDirectoryRow(snapshot, uid))
                    continue;
                var sortKey = MatchAllSortKey(snapshot, uid, worker, utf8);
                foreach (var row in snapshot.RowsForUid(uid))
                {
                    if ((flags[row] & deletedFlag) != 0 || (mayBeSuperseded && delta.IsSuperseded(row)))
                        continue;
                    if (membership != null && !RowMatchesFilter(snapshot, delta, row, ctx, membership))
                        continue;
                    if (!MatchesFileScope(snapshot, delta, row, fileNamePatterns))
                        continue;
                    topN.Add(new FzfRank(row, 0, sortKey));
                }
            }
            SearchMatcher.ReturnWorker(worker);
        }
        else
        {
            var hits = SearchMatcher.RentHitList();
            rentHits = hits;
            SearchMatcher.MatchUniques(snapshot, pattern, hits, token, fileNamePatterns);
            foreach (var m in hits)
            {
                foreach (var row in snapshot.RowsForUid(m.Uid))
                {
                    if ((flags[row] & deletedFlag) != 0 || (mayBeSuperseded && delta.IsSuperseded(row)))
                        continue;
                    if (membership != null && !RowMatchesFilter(snapshot, delta, row, ctx, membership))
                        continue;
                    if (!MatchesFileScope(snapshot, delta, row, fileNamePatterns))
                        continue;
                    // The per-unique sort key applies verbatim to every row of that unique --
                    // EntryIndex isn't packed into the key, so nothing is recomputed per row.
                    topN.Add(new FzfRank(row, m.Match.Score, m.SortKey));
                }
            }
        }

        MatchDeltaRows(snapshot, delta, pattern, matchAll, ctx, topN, fileNamePatterns);
        return rentHits;
    }

    private static bool MatchesFileScope(Snapshot snapshot, DeltaOverlay delta, int row, string[]? patterns)
        => patterns == null || IsDirectory(snapshot, delta, row) || FilterPatternHelper.Matches(delta.NameOf(row), patterns);
    private static bool IsDirectory(Snapshot snapshot, DeltaOverlay delta, int row)
        => delta.BaseOverrides.TryGetValue(row, out var record)
            ? (record.Flags & (ushort)FileRecordFlags.Directory) != 0
            : snapshot.IsDirectory(row);

    private static bool MatchesFileScope(string name, ushort flags, string[]? patterns)
        => patterns == null || (flags & (ushort)FileRecordFlags.Directory) != 0 || FilterPatternHelper.Matches(name, patterns);

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
    private static void MatchDeltaRows(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, bool matchAll, DirectoryContext ctx, FzfTopN topN, string[]? fileNamePatterns)
    {
        var slab = new FzfSlab();
        var queryLen = pattern.GetTotalTermLength();

        foreach (var (row, record) in delta.BaseOverrides)
        {
            if (record.Name.Length == 0)
                continue;
            if (!MatchesFileScope(record.Name, record.Flags, fileNamePatterns))
                continue;
            var match = EmptyPatternMatch;
            if (!matchAll && !SearchMatcherRow.TryMatchNameOrAliases(pattern, record.Name, record.Aliases, record.ProviderIds, record.Grids, queryLen, slab, out match))
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
            if (!MatchesFileScope(record.Name, record.Flags, fileNamePatterns))
                continue;
            var match = EmptyPatternMatch;
            if (!matchAll && !SearchMatcherRow.TryMatchNameOrAliases(pattern, record.Name, record.Aliases, record.ProviderIds, record.Grids, queryLen, slab, out match))
                continue;
            if (ctx.FilterLower != null && !delta.GetFullPath(record).StartsWith(ctx.FilterLower, StringComparison.OrdinalIgnoreCase))
                continue;
            topN.Add(FzfResultRank.ForDefaultScheme(snapshot.Count + i, record.Name, match));
        }
    }
}
