using System.Runtime.InteropServices;
using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.IndexV2.Search;

// Order-free "a term may be satisfied by an ancestor folder instead of the file name" pass, run by
// NameSearch to top up a page the names alone did not fill. That trigger is what keeps this cheap: a
// query that already answers in full never reaches it. Everything found here is appended after the
// name hits rather than merged with them, so the ranking can reuse the matched term's own sort key
// untouched -- no sort-key surgery, and no risk of pushing a genuine name match down.
//
// Distinct from PathSearch, which models a POSITIONAL "dir\subdir\file" query: there the query's
// segments must appear in ancestors in that same order. Here the terms carry no position at all, so
// each ancestor is offered every still-unsatisfied term (fzf terms are already order-free against a
// name; this extends the same property across the path).
//
// Deliberate v1 restriction: at least one term must match the FILE NAME. Without it the candidate set
// becomes "every row under any folder matching any term", which one common folder name turns
// into most of a drive -- and that set cannot be enumerated from phase A's per-term unique hits, which
// is exactly what keeps the row walk here bounded to the same order of work as an ordinary search.
internal static class PathTermFallback
{
    /// <summary>What one unique name contributed: which terms its own name satisfied, and the best
    /// match among them for a matching row to rank by.</summary>
    private struct NameHit
    {
        public int Mask;
        public int Score;
        public ulong SortKey;
    }

    // The two tables below hold one entry per unique name and one per folder walked, which on a broad
    // query is tens of thousands each. Built and thrown away per search, they were most of what this
    // pass allocated; rented and cleared, the buckets survive and only the first search of a size pays
    // for them. Pooled the way SearchMatcher pools its workers and hit lists, because searches on
    // different drives run at the same time and a single shared instance would be a race.
    private sealed class Scratch
    {
        public readonly Dictionary<int, NameHit> NameHits = new();
        public readonly Dictionary<int, int> AncestorMemo = new();
        public readonly List<int> AncestorChain = new(64);

        public void Reset()
        {
            // Trimmed rather than merely cleared: both dictionaries scale with the search (one entry per
            // matched name, one per directory walked), and Clear keeps the buckets, so a whole-drive
            // query would leave this pooled scratch sized for it forever. See SearchScratchPolicy.
            SearchScratchPolicy.ClearAndTrim(NameHits);
            SearchScratchPolicy.ClearAndTrim(AncestorMemo);
            AncestorChain.Clear();
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentBag<Scratch> ScratchPool = new();

    private static Scratch RentScratch()
    {
        var scratch = ScratchPool.TryTake(out var pooled) ? pooled : new Scratch();
        scratch.Reset();
        return scratch;
    }

    // The satisfied-term set is carried as a bitmask, so a query with more terms than bits simply
    // opts out rather than silently matching on a truncated set.
    private const int MaxTerms = 16;

    public static void SearchStreaming(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, int limit,
        Action<SearchResult> onResult, CancellationToken token, string? directoryFilterLower)
    {
        if (pattern.OrGroups != null)
        {
            SearchAndFirstBranches(snapshot, delta, pattern, limit, onResult, token, directoryFilterLower);
            return;
        }

        var termCount = pattern.TermSets.Length;
        // One term has nowhere to split: the "at least one term matches the name" rule would make this
        // identical to the name search that just came up empty.
        if (termCount < 2 || termCount > MaxTerms)
            return;

        var directoryContext = NameSearch.ResolveDirectoryContext(snapshot, delta, directoryFilterLower);
        if (directoryContext.Excluded)
            return;

        var termPatterns = new FzfPattern[termCount];
        var termBytePatterns = new FzfBytePattern[termCount];
        for (var i = 0; i < termCount; i++)
        {
            termPatterns[i] = FzfPattern.ForTermSet(pattern, i);
            termBytePatterns[i] = FzfBytePattern.From(termPatterns[i]);
        }

        // Phase A once per term instead of once per query: which unique names satisfy term i, and with
        // what sort key (kept so an emitted row can rank by the term that actually hit its name).
        //
        // One entry per unique rather than a mask table and a rank table side by side. Both were keyed
        // the same way and written in the same breath, so every hit paid four hash lookups where it
        // needs one, and the row walk below then looked the rank up a second time. A broad term set can
        // reach tens of thousands of uniques per query.
        var scratch = RentScratch();
        var nameHits = scratch.NameHits;
        var fullMask = (1 << termCount) - 1;
        for (var i = 0; i < termCount; i++)
        {
            token.ThrowIfCancellationRequested();
            var hits = SearchMatcher.RentHitList();
            SearchMatcher.MatchUniques(snapshot, termPatterns[i], hits, token);
            var bit = 1 << i;
            foreach (var m in hits)
            {
                ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(nameHits, m.Uid, out var existed);
                entry.Mask |= bit;
                // Keep the strongest term hit as the row's ranking basis.
                if (!existed || m.Match.Score > entry.Score)
                {
                    entry.Score = m.Match.Score;
                    entry.SortKey = m.SortKey;
                }
            }
            SearchMatcher.ReturnHitList(hits);
        }

        if (nameHits.Count == 0)
        {
            ScratchPool.Add(scratch);
            return;
        }

        // The source root's segments are the same for every row in the query, but the ancestor walk
        // below consulted them on each of its (many) memo misses -- re-splitting the root string and
        // re-matching every term against it tens of thousands of times per search. Computed once.
        var rootWorker = SearchMatcher.RentWorker();
        var rootMask = PathTermFallbackAncestorHelper.MaskFromSegments(snapshot.SourceRoot.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries), termPatterns, rootWorker, 0);
        SearchMatcher.ReturnWorker(rootWorker);

        var ancestorMemo = scratch.AncestorMemo;
        var ancestorChain = scratch.AncestorChain;
        var membership = directoryContext.FilterLower != null ? new Dictionary<int, bool>() : null;
        var worker = SearchMatcher.RentWorker();
        // Bounded by the index rather than by the caller's limit, which is no longer capped: the
        // multiply overflows int for a large enough limit, and FzfTopN reserves twice its capacity.
        var keep = (int)Math.Min((long)Math.Max(limit, 8) * 8, snapshot.Count + delta.Added.Count);
        var topN = new FzfTopN(keep);
        try
        {
            foreach (var (uid, hit) in nameHits)
            {
                token.ThrowIfCancellationRequested();
                // A name satisfying every term on its own is exactly what the name search matches, so
                // it has already been reported (or was cut by its limit, which this pass must not
                // undo by re-reporting it further down). Skipping is what keeps the two passes from
                // double-reporting now that this one runs alongside a non-empty result set.
                var nameMask = hit.Mask;
                if (nameMask == fullMask)
                    continue;

                foreach (var row in snapshot.RowsForUid(uid))
                {
                    if (snapshot.IsDeleted(row) || delta.IsSuperseded(row))
                        continue;
                    if (membership != null && !NameSearch.RowMatchesFilter(snapshot, delta, row, directoryContext, membership))
                        continue;

                    var parent = snapshot.ParentIndexes[row];
                    if (parent == row || parent < 0)
                        continue;
                    if ((nameMask | PathTermFallbackAncestorHelper.AncestorMask(snapshot, delta, parent, termPatterns, termBytePatterns, worker, ancestorMemo, fullMask, rootMask, ancestorChain)) != fullMask)
                        continue;

                    topN.Add(new FzfRank(row, hit.Score, hit.SortKey));
                }
            }
        }
        finally
        {
            SearchMatcher.ReturnWorker(worker);
            // Returned before the results are emitted: nothing below reads it, and a caller that stops
            // consuming partway through must not strand it.
            ScratchPool.Add(scratch);
        }

        var emitted = 0;
        var seen = new HashSet<int>();
        foreach (var rank in topN.Finish(keep))
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(rank.EntryIndex))
                continue;
            onResult(ResultBuilder.ToResult(snapshot, delta, rank));
            if (++emitted >= limit)
                break;
        }
    }

    private static void SearchAndFirstBranches(Snapshot snapshot, DeltaOverlay delta, FzfPattern pattern, int limit,
        Action<SearchResult> onResult, CancellationToken token, string? directoryFilterLower)
    {
        var emitted = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in pattern.OrGroups!)
        {
            if (emitted >= limit)
                break;

            // The existing fallback algorithm operates on an AND list of term sets. Reusing it per DNF
            // branch preserves its bounded name/ancestor walk while the outer callback deduplicates rows
            // that satisfy more than one OR branch.
            var branch = new FzfPattern(pattern.TargetDrive, group.Sets);
            SearchStreaming(snapshot, delta, branch, limit, result =>
            {
                if (emitted >= limit || !seen.Add(result.Path))
                    return;
                emitted++;
                onResult(result);
            }, token, directoryFilterLower);
        }
    }

}
