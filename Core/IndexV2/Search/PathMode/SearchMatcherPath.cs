using System.Text;
using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.IndexV2.Search.PathMode;

// Path-mode phase-A output: the per-unique rank ingredients (low 32 sort-key bits + char length)
// path mode combines with per-row directory scores -- see PathSearchFuzzy.
internal readonly record struct PathUniqueMatch(int Uid, FzfPatternResult Match, uint RankLow32, int NameLen);

// Path mode's phase A: per-unique file-part matching with the SAME machinery as name mode
// (SearchMatcher), but carrying the rank ingredients path mode needs per row instead of a finished
// name-mode sort key. A null pattern (dir-only query like "src\") admits every unique, mirroring the
// old filePattern == null branch.
internal static class SearchMatcherPath
{
    internal static List<PathUniqueMatch> MatchUniquesForPath(Snapshot snapshot, FzfPattern? pattern)
    {
        var merged = new List<PathUniqueMatch>();
        if (pattern == null)
        {
            var worker = SearchMatcher.RentWorker();
            for (var uid = 0; uid < snapshot.UniqueCount; uid++)
            {
                var utf8 = snapshot.UniqueNameUtf8(uid);
                if (utf8.Length == 0)
                    continue;
                merged.Add(new PathUniqueMatch(uid, default, 0xFFFFFFFFu, NameCharLength(snapshot, uid, worker, utf8)));
            }
            SearchMatcher.ReturnWorker(worker);
            return merged;
        }

        var ctx = SearchMatcher.BuildContext(pattern);
        var chunkCount = (snapshot.UniqueCount + SearchMatcher.ChunkSize - 1) / SearchMatcher.ChunkSize;
        var perChunk = new List<PathUniqueMatch>?[Math.Max(chunkCount, 1)];

        Parallel.For(
            0,
            Math.Max(chunkCount, 1),
            SearchMatcher.RentWorker,
            (chunk, _, worker) =>
            {
                var hits = new List<PathUniqueMatch>();
                var start = chunk * SearchMatcher.ChunkSize;
                var end = Math.Min(start + SearchMatcher.ChunkSize, snapshot.UniqueCount);
                var masks = snapshot.UniqueMasks;
                for (var uid = start; uid < end; uid++)
                {
                    if (ctx.CanFilter && ((masks[uid] & ctx.RequiredMask) != ctx.RequiredMask || !SearchMatcher.PassesOrSets(masks[uid], ctx.OrSetMasks)))
                        continue;
                    PathMatchOne(snapshot, ctx, uid, worker, hits);
                }
                perChunk[chunk] = hits;
                return worker;
            },
            SearchMatcher.ReturnWorker);

        foreach (var hits in perChunk)
        {
            if (hits != null)
                merged.AddRange(hits);
        }
        return merged;
    }

    private static void PathMatchOne(Snapshot snapshot, SearchMatcher.QueryContext ctx, int uid, SearchMatcher.Worker worker, List<PathUniqueMatch> hits)
    {
        var utf8 = snapshot.UniqueNameUtf8(uid);
        if (utf8.Length == 0)
            return;

        if (snapshot.IsUniqueAscii(uid))
        {
            if (ctx.BytePattern.TryMatch(utf8, out var byteMatch, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers))
            {
                hits.Add(new PathUniqueMatch(uid, byteMatch, FzfBytePattern.RankLow32(utf8, byteMatch), utf8.Length));
                return;
            }
            if (snapshot.HasAliases(uid) && SearchMatcher.TryMatchAliases(snapshot, ctx, uid, worker, out var aliasBest))
                hits.Add(new PathUniqueMatch(uid, aliasBest, FzfBytePattern.RankLow32(utf8, aliasBest), utf8.Length));
            return;
        }

        if (worker.Scratch.Length < utf8.Length)
            worker.Scratch = new char[Math.Max(utf8.Length, worker.Scratch.Length * 2)];
        var written = Encoding.UTF8.GetChars(utf8, worker.Scratch);
        var name = worker.Scratch.AsSpan(0, written);

        if (ctx.Pattern.TryMatch(name, out var match, FzfScoringScheme.Default, worker.Slab))
        {
            hits.Add(new PathUniqueMatch(uid, match, FzfResultRank.RankLow32(name, match), written));
        }
        else if (snapshot.HasAliases(uid) && SearchMatcher.TryMatchAliases(snapshot, ctx, uid, worker, out var best))
        {
            hits.Add(new PathUniqueMatch(uid, best, FzfResultRank.RankLow32(name, best), written));
        }
    }

    private static int NameCharLength(Snapshot snapshot, int uid, SearchMatcher.Worker worker, ReadOnlySpan<byte> utf8)
    {
        if (snapshot.IsUniqueAscii(uid))
            return utf8.Length;
        if (worker.Scratch.Length < utf8.Length)
            worker.Scratch = new char[Math.Max(utf8.Length, worker.Scratch.Length * 2)];
        return Encoding.UTF8.GetChars(utf8, worker.Scratch);
    }
}
