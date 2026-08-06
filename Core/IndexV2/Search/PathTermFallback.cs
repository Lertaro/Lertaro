using System.Runtime.InteropServices;
using System.Text;
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
        var rootMask = MaskFromSegments(snapshot.SourceRoot.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries), termPatterns, rootWorker, 0);
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
                    if (membership != null && !NameSearch.RowMatchesFilter(snapshot, delta, row, delta.GetFullPath(row), directoryContext, membership))
                        continue;

                    var parent = snapshot.ParentIndexes[row];
                    if (parent == row || parent < 0)
                        continue;
                    if ((nameMask | AncestorMask(snapshot, delta, parent, termPatterns, termBytePatterns, worker, ancestorMemo, fullMask, rootMask, ancestorChain)) != fullMask)
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

    // Which terms this parent's ancestor chain satisfies. Memoized on the parent row alone -- the
    // verdict depends only on the chain, never on the file sitting in it, so every file in a folder
    // (and every folder under an already-walked one) reuses the same answer. Mirrors PathGate's walk:
    // stop at a negative or self parent, skip empty names, then offer the source root's own segments.
    private static int AncestorMask(Snapshot snapshot, DeltaOverlay delta, int parentRow,
        FzfPattern[] termPatterns, FzfBytePattern[] termBytePatterns, SearchMatcher.Worker worker,
        Dictionary<int, int> memo, int fullMask, int rootMask, List<int> chain)
    {
        if (memo.TryGetValue(parentRow, out var cached))
            return cached;

        // Walk up collecting the chain until something already known is reached, then unwind and record
        // an answer for EVERY node on the way rather than only the one asked about. Ancestor chains
        // overlap heavily -- two files in different folders still share everything above the first
        // common parent -- so memoising the entry point alone re-walked those shared upper segments once
        // per distinct folder. Measured at a 78% miss rate on a real query, because with a few files per
        // folder almost every row arrives with a parent nobody has asked about yet.
        chain.Clear();
        var mask = 0;
        var current = parentRow;
        var composable = false;
        // Bounded by the row count rather than a fixed 512. The bound is only there to stop a corrupt
        // parent cycle spinning forever, and an acyclic chain cannot be longer than the number of rows,
        // so this never cuts a real one short -- while 512 did, and once answers are shared that became
        // order-dependent: a walk that truncated returned less than the same walk did after a shallower
        // one had already filled in the folders above it. Same query, different answer depending on
        // which row the pass happened to reach first.
        for (var depth = 0; depth < snapshot.Count && current >= 0; depth++)
        {
            if (memo.TryGetValue(current, out var known))
            {
                mask = known;
                composable = true;
                break;
            }

            if (delta.IsSuperseded(current))
            {
                // A renamed/overridden ancestor's live name only exists in delta state, so fall back to
                // the built path string for the whole chain -- the same escape hatch PathGate takes.
                // That answer describes the chain from parentRow specifically and says nothing about the
                // nodes above it, so it is the one case that cannot be shared.
                var fallback = MaskFromPath(delta.GetFullPath(parentRow), termPatterns, worker);
                if (fallback != fullMask)
                    fallback |= rootMask;
                memo[parentRow] = fallback;
                return fallback;
            }

            chain.Add(current);
            var parent = snapshot.ParentIndexes[current];
            if (parent == current)
            {
                composable = true;
                break;
            }
            current = parent;
        }

        // Unwound from the top down, so each node sees what its own ancestors already satisfy and can be
        // recorded with a complete answer. Nodes are skipped once everything above them already matches
        // every term, exactly as the old walk stopped early.
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var node = chain[i];
            if (mask != fullMask)
            {
                var uid = (int)snapshot.NameIds[node];
                var nameUtf8 = snapshot.UniqueNameUtf8(uid);
                if (nameUtf8.Length > 0)
                    mask |= MaskForSegment(snapshot, uid, nameUtf8, termPatterns, termBytePatterns, worker, mask, fullMask);
            }

            // Only when the walk ended at the root or at a known node. Hitting the depth guard means the
            // chain was truncated, and a truncated answer must not be handed to a node further up whose
            // own walk would have reached higher.
            if (composable)
                memo[node] = mask | rootMask;
        }

        if (mask != fullMask)
            mask |= rootMask;

        memo[parentRow] = mask;
        return mask;
    }

    private static int MaskForSegment(Snapshot snapshot, int uid, ReadOnlySpan<byte> nameUtf8,
        FzfPattern[] termPatterns, FzfBytePattern[] termBytePatterns, SearchMatcher.Worker worker, int already, int fullMask)
    {
        var mask = 0;
        var ascii = snapshot.IsUniqueAscii(uid);
        var written = 0;
        if (!ascii)
        {
            if (worker.Scratch.Length < nameUtf8.Length)
                worker.Scratch = new char[Math.Max(nameUtf8.Length, worker.Scratch.Length * 2)];
            written = Encoding.UTF8.GetChars(nameUtf8, worker.Scratch);
        }

        for (var i = 0; i < termPatterns.Length; i++)
        {
            var bit = 1 << i;
            if ((already & bit) != 0)
                continue; // already satisfied deeper in the chain
            var hit = ascii
                ? termBytePatterns[i].TryMatch(nameUtf8, out _, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers)
                : termPatterns[i].TryMatch(worker.Scratch.AsSpan(0, written), out _, FzfScoringScheme.Default, worker.Slab);
            if (hit)
                mask |= bit;
        }

        var unresolved = fullMask & ~(already | mask);
        if (unresolved != 0)
            mask |= MaskFromAliases(snapshot, uid, termPatterns, termBytePatterns, worker, unresolved);
        return mask;
    }

    // Baked-alias fallback, mirroring PathGate's: without it a folder named in a non-Latin script can
    // only ever be reached by typing its literal name, which defeats the whole point for a CJK library
    // ("dcj" has to reach a folder whose pinyin initials are d-c-j). Aliases are walked once and each
    // one offered every still-unresolved term, so a segment with many readings decodes at most once per
    // alias rather than once per term. Ungated and first-match-wins, matching PathGate.
    private static int MaskFromAliases(Snapshot snapshot, int uid, FzfPattern[] termPatterns,
        FzfBytePattern[] termBytePatterns, SearchMatcher.Worker worker, int unresolved)
    {
        var mask = 0;
        var disabledIds = SearchContext.DisabledAliasIds;
        var (start, end) = snapshot.AliasEntryRange(uid);
        for (var e = start; e < end && mask != unresolved; e++)
        {
            if (disabledIds != null && disabledIds.Contains(snapshot.AliasProviderId(e)))
                continue;
            var aliasUtf8 = snapshot.AliasUtf8(e);
            if (aliasUtf8.Length == 0)
                continue;

            var ascii = Ascii.IsValid(aliasUtf8);
            var written = 0;
            if (!ascii)
            {
                if (worker.AliasScratch.Length < aliasUtf8.Length)
                    worker.AliasScratch = new char[Math.Max(aliasUtf8.Length, worker.AliasScratch.Length * 2)];
                written = Encoding.UTF8.GetChars(aliasUtf8, worker.AliasScratch);
            }

            for (var i = 0; i < termPatterns.Length; i++)
            {
                var bit = 1 << i;
                if ((unresolved & bit) == 0 || (mask & bit) != 0)
                    continue;
                // TryMatchSegmented on the byte side: one alias string can hold several polyphonic
                // readings joined by '|', and a term must land inside a single reading.
                var hit = ascii
                    ? termBytePatterns[i].TryMatchSegmented(aliasUtf8, out _, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers)
                    : termPatterns[i].TryMatch(worker.AliasScratch.AsSpan(0, written), out _, FzfScoringScheme.Default, worker.Slab);
                if (hit)
                    mask |= bit;
            }
        }
        return mask;
    }

    private static int MaskFromPath(string path, FzfPattern[] termPatterns, SearchMatcher.Worker worker)
        => MaskFromSegments(path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries), termPatterns, worker, 0);

    private static int MaskFromSegments(string[] segments, FzfPattern[] termPatterns, SearchMatcher.Worker worker, int already)
    {
        var mask = 0;
        foreach (var segment in segments)
        {
            for (var i = 0; i < termPatterns.Length; i++)
            {
                var bit = 1 << i;
                if (((already | mask) & bit) != 0)
                    continue;
                if (termPatterns[i].TryMatch(segment, out _, FzfScoringScheme.Default, worker.Slab))
                    mask |= bit;
            }
        }
        return mask;
    }
}
