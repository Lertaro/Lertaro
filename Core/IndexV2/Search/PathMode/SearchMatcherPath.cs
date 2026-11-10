using System.Text;
using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.Services.Plugin.DirectoryIndex;
using Lertaro.PluginSdk.Helpers;
namespace Lertaro.Core.IndexV2.Search.PathMode;

// Path-mode phase-A output: the per-unique rank ingredients (low 32 sort-key bits + char length)
// path mode combines with per-row directory scores -- see PathSearchFuzzy.
internal readonly record struct PathUniqueMatch(int Uid, int Score, uint RankLow32, int NameLen, bool AdmitsFile = true);

// Path mode's phase A: per-unique file-part matching with the SAME machinery as name mode
// (SearchMatcher), but carrying the rank ingredients path mode needs per row instead of a finished
// name-mode sort key. A null pattern (dir-only query like "src\") admits every unique, mirroring the
// old filePattern == null branch.
internal static class SearchMatcherPath
{
    // token has to reach ParallelOptions (and the dir-only loop below) for the same reason it does in
    // SearchMatcher.MatchUniques: a path-mode scan of a whole drive can touch most of the unique table, and
    // without it a scan the next keystroke already superseded still runs to completion -- several abandoned
    // scans at once, each pinning every core. See the comment on that method.
    internal static List<PathUniqueMatch> MatchUniquesForPath(Snapshot snapshot, FzfPattern? pattern, CancellationToken token = default,
        string[]? fileNamePatterns = null, SearchCategoryFilter category = SearchCategoryFilter.All)
    {
        var merged = new List<PathUniqueMatch>();
        var admitsDirectory = category is SearchCategoryFilter.All or SearchCategoryFilter.Folder;
        bool AdmitsFile(int uid, SearchMatcher.Worker worker)
        {
            if (category == SearchCategoryFilter.Folder) return false;
            if (category != SearchCategoryFilter.All)
                return FilterPatternHelper.MatchesSearchResult(snapshot.GetUniqueName(uid), false, fileNamePatterns, category);
            if (fileNamePatterns == null) return true;
            // Decode into the existing worker buffer: a scope check needs no retained name string.
            var utf8 = snapshot.UniqueNameUtf8(uid);
            if (worker.Scratch.Length < utf8.Length) worker.Scratch = new char[Math.Max(utf8.Length, worker.Scratch.Length * 2)];
            var written = Encoding.UTF8.GetChars(utf8, worker.Scratch);
            return FilterPatternHelper.Matches(worker.Scratch.AsSpan(0, written), fileNamePatterns);
        }
        if (pattern == null)
        {
            var worker = SearchMatcher.RentWorker();
            try
            {
                for (var uid = 0; uid < snapshot.UniqueCount; uid++)
                {
                    // The dir-only query takes the serial branch, so it needs the check itself rather than
                    // getting it from ParallelOptions. Cheap on an already-cancelled token, and this loop is
                    // over every unique name on the drive.
                    token.ThrowIfCancellationRequested();
                    var admitsFile = AdmitsFile(uid, worker);
                    if (!admitsFile && !(admitsDirectory && SearchMatcher.HasDirectoryRow(snapshot, uid))) continue;
                    var utf8 = snapshot.UniqueNameUtf8(uid);
                    if (utf8.Length == 0)
                        continue;
                    merged.Add(new PathUniqueMatch(uid, default, 0xFFFFFFFFu, NameCharLength(snapshot, uid, worker, utf8), admitsFile));
                }
            }
            finally { SearchMatcher.ReturnWorker(worker); }
            return merged;
        }

        var ctx = SearchMatcher.BuildContext(pattern);
        var chunkCount = (snapshot.UniqueCount + SearchMatcher.ChunkSize - 1) / SearchMatcher.ChunkSize;
        var perChunk = new List<PathUniqueMatch>?[Math.Max(chunkCount, 1)];

        Parallel.For(
            0,
            Math.Max(chunkCount, 1),
            new ParallelOptions { CancellationToken = token },
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
                    token.ThrowIfCancellationRequested();
                    var admitsFile = AdmitsFile(uid, worker);
                    if (!admitsFile && !(admitsDirectory && SearchMatcher.HasDirectoryRow(snapshot, uid))) continue;
                    PathMatchOne(snapshot, ctx, uid, worker, hits, admitsFile);
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

    private static void PathMatchOne(Snapshot snapshot, SearchMatcher.QueryContext ctx, int uid, SearchMatcher.Worker worker, List<PathUniqueMatch> hits, bool admitsFile)
    {
        var utf8 = snapshot.UniqueNameUtf8(uid);
        if (utf8.Length == 0)
            return;

        // Pure-ASCII name: bytes ARE the chars (same values, same offsets) -- match with zero decode.
        // Only when the query has no regex clause: the byte matcher cannot apply one, so for a regex-only
        // pattern every ASCII name would be rejected (the byte pattern has no positive term to match on)
        // and for a term+regex pattern the clause would be silently skipped. Regex queries decode instead.
        // This mirrors SearchMatcher.MatchOne, the name-mode twin of this method.
        if (snapshot.IsUniqueAscii(uid) && !ctx.BytePattern.HasRegexClauses)
        {
            if (ctx.BytePattern.TryMatch(utf8, out var byteMatch, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers))
            {
                hits.Add(new PathUniqueMatch(uid, byteMatch.Score, FzfBytePattern.RankLow32(utf8, byteMatch), utf8.Length, admitsFile));
                return;
            }
            if (snapshot.HasAliases(uid) && SearchMatcher.TryMatchAliases(snapshot, ctx, uid, worker, SearchMatcher.DecodeName(worker, utf8), out var aliasBest))
                hits.Add(new PathUniqueMatch(uid, aliasBest.Score, FzfBytePattern.RankLow32(utf8, aliasBest), utf8.Length, admitsFile));
            return;
        }

        var name = SearchMatcher.DecodeName(worker, utf8);

        // The live reading tier first, exactly as in SearchMatcher.MatchOne; without a live provider
        // the name-tier match runs as always. Looked up once per search on ctx, not per candidate.
        var live = ctx.Live;
        if (live.HasProviders)
        {
            if (live.TryScore(name, worker.Slab, out var liveMatch))
            {
                hits.Add(new PathUniqueMatch(uid, liveMatch.Score, FzfResultRank.RankLow32(name, liveMatch), name.Length, admitsFile));
                return;
            }
        }
        else if (ctx.Pattern.TryMatch(name, out var match, FzfScoringScheme.Default, worker.Slab))
        {
            hits.Add(new PathUniqueMatch(uid, match.Score, FzfResultRank.RankLow32(name, match), name.Length, admitsFile));
            return;
        }

        // Same veto as SearchMatcher.MatchOne: a `:term` can only be checked against the name, and the
        // alias tier below would otherwise revive the candidate the name match rejected. The live tier
        // above answers both name-level clauses; the alias tier reads the name for the `/.../` half itself.
        if (ctx.Pattern.HasExclusions && !ctx.Pattern.ExclusionsPass(name))
            return;

        if (snapshot.HasAliases(uid) && SearchMatcher.TryMatchAliases(snapshot, ctx, uid, worker, name, out var best))
        {
            hits.Add(new PathUniqueMatch(uid, best.Score, FzfResultRank.RankLow32(name, best), name.Length, admitsFile));
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
