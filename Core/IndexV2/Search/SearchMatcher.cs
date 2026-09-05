using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.Core.Services.Plugin.DirectoryIndex;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.IndexV2.Search;

internal readonly record struct UniqueMatch(int Uid, FzfPatternResult Match, ulong SortKey);

// Phase A of name search: match every UNIQUE name in the snapshot against the pattern. Pure-ASCII
// names (baked bit, Snapshot.IsUniqueAscii) match on their raw UTF-8 bytes with zero decode
// (FzfBytePattern); the rest decode once into the worker's reusable scratch and match as spans -- no
// string is materialized per candidate, and the rank sort key is computed per-unique from the hot
// span (fanned out per row by NameSearch, since EntryIndex isn't packed into the key). The charmask
// prefilter covers multi-term OR sets (per-set "any term's mask covered") and its scan is
// AVX2-vectorized. Workers (slab + scratches + hit list) pool across searches. Delta rows (renamed/
// added, not in the unique table) are matched separately -- see SearchMatcherRow / NameSearch.
internal static class SearchMatcher
{
    internal const int ChunkSize = 8192;

    internal sealed class Worker
    {
        public readonly FzfSlab Slab = new();
        public readonly FzfByteBuffers ByteBuffers = new();
        public readonly List<UniqueMatch> Hits = new();
        public char[] Scratch = new char[256];
        public char[] AliasScratch = new char[256];
    }

    internal sealed class QueryContext
    {
        public required FzfPattern Pattern;
        public required FzfBytePattern BytePattern;
        public required ulong RequiredMask;   // union of single-term sets' masks: candidate must contain all
        public required ulong[][] OrSetMasks; // per multi-term set: candidate must cover at least one term
        public required bool CanFilter;
        public required int QueryLen;
        public required MixedTerm? MixedTerm; // non-null only for a bare single term mixing an alias provider's own two alphabets
    }

    private static readonly ConcurrentBag<Worker> WorkerPool = new();

    internal static Worker RentWorker() => WorkerPool.TryTake(out var pooled) ? pooled : new Worker();

    internal static void ReturnWorker(Worker worker)
    {
        // The worker itself is still worth pooling (its slab and byte buffers are bounded by name
        // length, not by how many rows matched); only its hit list scales with the search.
        SearchScratchPolicy.ClearAndTrim(worker.Hits);
        WorkerPool.Add(worker);
    }

    internal static QueryContext BuildContext(FzfPattern pattern)
    {
        ulong requiredMask = 0;
        List<ulong[]>? orSets = null;
        foreach (var set in pattern.TermSets)
        {
            // Any inverse term makes its whole set unfilterable (absence can't be mask-tested).
            var filterable = true;
            foreach (var term in set.Terms)
                filterable &= !term.Inverse;
            if (!filterable)
                continue;

            if (set.Terms.Length == 1)
            {
                requiredMask |= FzfAlgorithm.GetCharMask(set.Terms[0].Text);
            }
            else
            {
                var masks = new ulong[set.Terms.Length];
                for (var t = 0; t < set.Terms.Length; t++)
                    masks[t] = FzfAlgorithm.GetCharMask(set.Terms[t].Text);
                (orSets ??= new List<ulong[]>()).Add(masks);
            }
        }

        return new QueryContext
        {
            Pattern = pattern,
            BytePattern = FzfBytePattern.From(pattern),
            RequiredMask = requiredMask,
            OrSetMasks = orSets?.ToArray() ?? Array.Empty<ulong[]>(),
            CanFilter = requiredMask != 0 || orSets != null,
            QueryLen = pattern.GetTotalTermLength(),
            MixedTerm = MixedQueryMatcher.TrySegmentPattern(pattern),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool PassesOrSets(ulong candidateMask, ulong[][] orSets)
    {
        foreach (var masks in orSets)
        {
            var any = false;
            foreach (var m in masks)
            {
                if ((candidateMask & m) == m)
                {
                    any = true;
                    break;
                }
            }
            if (!any)
                return false;
        }
        return true;
    }

    // Pooled result lists: a broad single-char query's hit list reaches hundreds of thousands of
    // entries on a large drive, so reallocating it per keystroke was the last per-search allocation
    // of any size. Callers rent, consume, and return.
    private static readonly ConcurrentBag<List<UniqueMatch>> HitListPool = new();

    internal static List<UniqueMatch> RentHitList() => HitListPool.TryTake(out var pooled) ? pooled : new List<UniqueMatch>();

    internal static void ReturnHitList(List<UniqueMatch> list)
    {
        SearchScratchPolicy.ClearAndTrim(list);
        HitListPool.Add(list);
    }

    internal static void MatchUniques(Snapshot snapshot, FzfPattern pattern, List<UniqueMatch> merged, CancellationToken token = default, string[]? fileNamePatterns = null)
    {
        var ctx = BuildContext(pattern);
        merged.Clear();
        var mergeLock = new object();
        var chunkCount = (snapshot.UniqueCount + ChunkSize - 1) / ChunkSize;

        // A broad/low-selectivity query (e.g. a single character) can touch a large fraction of the
        // whole unique-name table before this returns -- during normal rapid typing, every keystroke's
        // scan used to run to completion regardless of whether a newer keystroke had already superseded
        // it, piling up CPU contention across several abandoned scans at once. The CancellationToken
        // here lets a superseded scan abort between chunks instead of always running to the end.
        Parallel.For(
            0,
            Math.Max(chunkCount, 1),
            new ParallelOptions { CancellationToken = token },
            RentWorker,
            (chunk, _, worker) =>
            {
                var start = chunk * ChunkSize;
                var end = Math.Min(start + ChunkSize, snapshot.UniqueCount);
                var masks = snapshot.UniqueMasks;

                if (ctx.CanFilter && ctx.RequiredMask != 0 && Avx2.IsSupported && end - start >= 8)
                {
                    // Vectorized prefilter: 4 masks per iteration; only lanes whose mask covers the
                    // whole required set fall through to the scalar per-candidate work.
                    ref var m0 = ref MemoryMarshal.GetReference(masks);
                    var required = Vector256.Create(ctx.RequiredMask);
                    var i = start;
                    for (; i + 4 <= end; i += 4)
                    {
                        var v = Vector256.LoadUnsafe(ref Unsafe.Add(ref m0, i));
                        var bits = Vector256.Equals(Vector256.BitwiseAnd(v, required), required).ExtractMostSignificantBits();
                        if (bits == 0)
                            continue;
                        for (var lane = 0; lane < 4; lane++)
                        {
                            if ((bits & (1u << lane)) != 0 && PassesOrSets(masks[i + lane], ctx.OrSetMasks))
                                MatchOne(snapshot, ctx, i + lane, worker, fileNamePatterns);
                        }
                    }
                    for (; i < end; i++)
                    {
                        if ((masks[i] & ctx.RequiredMask) == ctx.RequiredMask && PassesOrSets(masks[i], ctx.OrSetMasks))
                            MatchOne(snapshot, ctx, i, worker, fileNamePatterns);
                    }
                }
                else
                {
                    for (var uid = start; uid < end; uid++)
                    {
                        if (ctx.CanFilter && ((masks[uid] & ctx.RequiredMask) != ctx.RequiredMask || !PassesOrSets(masks[uid], ctx.OrSetMasks)))
                            continue;
                        MatchOne(snapshot, ctx, uid, worker, fileNamePatterns);
                    }
                }
                return worker;
            },
            worker =>
            {
                lock (mergeLock)
                {
                    merged.AddRange(worker.Hits);
                }
                ReturnWorker(worker);
            });
    }

    private static void MatchOne(Snapshot snapshot, QueryContext ctx, int uid, Worker worker, string[]? fileNamePatterns)
    {
        var utf8 = snapshot.UniqueNameUtf8(uid);
        if (utf8.Length == 0)
            return;
        if (fileNamePatterns != null
            && !FilterPatternHelper.Matches(snapshot.GetUniqueName(uid), fileNamePatterns)
            && !HasDirectoryRow(snapshot, uid))
            return;

        // Pure-ASCII name: bytes ARE the chars (same values, same offsets) -- match with zero decode.
        if (snapshot.IsUniqueAscii(uid))
        {
            if (ctx.BytePattern.TryMatch(utf8, out var byteMatch, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers))
            {
                worker.Hits.Add(new UniqueMatch(uid, byteMatch, FzfBytePattern.ForDefaultScheme(uid, utf8, byteMatch).SortKey));
                return;
            }
            if (snapshot.HasAliases(uid) && TryMatchAliases(snapshot, ctx, uid, worker, out var aliasBest))
                worker.Hits.Add(new UniqueMatch(uid, aliasBest, FzfBytePattern.ForDefaultScheme(uid, utf8, aliasBest).SortKey));
            return;
        }

        if (worker.Scratch.Length < utf8.Length)
            worker.Scratch = new char[Math.Max(utf8.Length, worker.Scratch.Length * 2)];
        var written = Encoding.UTF8.GetChars(utf8, worker.Scratch);
        var name = worker.Scratch.AsSpan(0, written);

        if (ctx.Pattern.TryMatch(name, out var match, FzfScoringScheme.Default, worker.Slab))
        {
            worker.Hits.Add(new UniqueMatch(uid, match, FzfResultRank.ForDefaultScheme(uid, name, match).SortKey));
        }
        else if (snapshot.HasAliases(uid) && TryMatchAliases(snapshot, ctx, uid, worker, out var best))
        {
            worker.Hits.Add(new UniqueMatch(uid, best, FzfResultRank.ForDefaultScheme(uid, name, best).SortKey));
        }
        else if (ctx.MixedTerm != null && snapshot.HasAliases(uid) && SearchMatcherAliasExtensions.TryMatchMixed(snapshot, ctx, uid, name, out var mixedBest))
        {
            worker.Hits.Add(new UniqueMatch(uid, mixedBest, FzfResultRank.ForDefaultScheme(uid, name, mixedBest).SortKey));
        }
    }

    internal static bool HasDirectoryRow(Snapshot snapshot, int uid)
    {
        foreach (var row in snapshot.RowsForUid(uid))
        {
            if (snapshot.IsDirectory(row))
                return true;
        }
        return false;
    }

    // Zero-copy alias fallback, also called directly by SearchMatcherPath: forwards to
    // SearchMatcherAliasExtensions, which holds this tier's implementation alongside the
    // mixed-alphabet last-resort tier (TryMatchMixed, used only from MatchOne above) -- split out
    // there (composition, not a partial class) to keep this file under the project's line limit.
    internal static bool TryMatchAliases(Snapshot snapshot, QueryContext ctx, int uid, Worker worker, out FzfPatternResult best)
        => SearchMatcherAliasExtensions.TryMatchAliases(snapshot, ctx, uid, worker, out best);
}
