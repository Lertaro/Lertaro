using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.IndexV2.Search;

// PathTermFallback needs to know which terms each name satisfies. Running MatchUniques once per term
// scans and decodes the same unique-name table repeatedly; this pass walks the table once per chunk and
// offers every term to each name. The per-term results retain the same TryMatchOne semantics used by the
// ordinary whole-query scan.
internal static partial class SearchMatcher
{
    internal static void MatchUniqueTerms(
        Snapshot snapshot,
        IReadOnlyList<FzfPattern> patterns,
        IReadOnlyList<List<UniqueMatch>> merged,
        CancellationToken token)
    {
        if (patterns.Count != merged.Count)
            throw new ArgumentException("Each term needs one result list.", nameof(merged));

        for (var i = 0; i < merged.Count; i++)
            merged[i].Clear();

        var contexts = new QueryContext[patterns.Count];
        for (var i = 0; i < patterns.Count; i++)
            contexts[i] = BuildContext(patterns[i]);

        var chunkCount = (snapshot.UniqueCount + ChunkSize - 1) / ChunkSize;
        var mergeLock = new object();
        Parallel.For(
            0,
            Math.Max(chunkCount, 1),
            new ParallelOptions { CancellationToken = token },
            RentWorker,
            (chunk, _, worker) =>
            {
                var termHits = worker.TermHits;
                if (termHits == null || termHits.Length != contexts.Length)
                    worker.TermHits = termHits = new List<UniqueMatch>[contexts.Length];
                for (var i = 0; i < termHits.Length; i++)
                {
                    termHits[i] ??= new List<UniqueMatch>();
                    termHits[i].Clear();
                }

                var start = chunk * ChunkSize;
                var end = Math.Min(start + ChunkSize, snapshot.UniqueCount);
                var masks = snapshot.UniqueMasks;
                for (var uid = start; uid < end; uid++)
                {
                    token.ThrowIfCancellationRequested();
                    var candidateMask = masks[uid];
                    for (var term = 0; term < contexts.Length; term++)
                    {
                        var ctx = contexts[term];
                        if (ctx.CanFilter
                            && ((ctx.RequiredMask != 0 && (candidateMask & ctx.RequiredMask) != ctx.RequiredMask)
                                || (ctx.OrSetMasks.Length != 0 && !PassesOrSets(candidateMask, ctx.OrSetMasks))))
                            continue;

                        if (TryMatchOne(snapshot, ctx, uid, worker, fileNamePatterns: null, out var hit))
                            termHits[term].Add(hit);
                    }
                }
                return worker;
            },
            worker =>
            {
                lock (mergeLock)
                {
                    for (var i = 0; i < merged.Count; i++)
                    {
                        var hits = worker.TermHits![i];
                        merged[i].AddRange(hits);
                        SearchScratchPolicy.ClearAndTrim(hits);
                    }
                }
                ReturnWorker(worker);
            });
    }
}
