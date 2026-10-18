using System.IO;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.ViewModels.Search.Mapping;

// The one ordering ladder every result list shares: candidates in, best-first rows out.
//
// Split out of SearchResultMapper purely to keep that file under the repo's per-file line limit. It has no
// state of its own -- it always operates on the candidate list its caller built.
internal static class RankedCandidateOrdering
{
    // User signals first, then match quality, then a stable tie-break:
    //
    //   * IsCurated / Priority -- favorites and history ("you use this"), which outrank any textual match.
    //   * TypeRank              -- the user's own result-type order (quick window only).
    //   * Match.Start           -- "left-side match priority": the query hitting furthest left in the name
    //                              leads the match keys. It used to sit behind Shape/RecencyBand/Depth, and
    //                              on a broad query the display cap then filled with rows that merely
    //                              matched a whole word or were touched today, cutting off the row the user
    //                              was looking for (reported as a plain drop in hit rate).
    //   * Match.Weight          -- then coverage*contiguity.
    //   * Match.Tier            -- then the WEAKEST match key: a literal name hit beats a shorthand alias
    //                              hit beats a full transliteration (英文 > 简拼 > 全拼). Placed last so it
    //                              only separates rows that already agree on position and coverage;
    //                              see AliasMatchRules.TierFor.
    //   * files before folders  -- only when the match is otherwise equal.
    //   * path length, path     -- then the stable, deterministic tail.
    //
    // Match.Shape, RecencyBand and Depth are still computed and carried on the candidate (RankSignals for
    // index rows) but deliberately order nothing since that report -- a consumer that wants them has them,
    // and none of them may push a left-hand hit off the screen.
    //
    // SearchResultRelevance packs the same match keys into RankSortKey for the full window and the engine.
    //
    // The inline window layers directory proximity ABOVE all of this separately (see
    // ExplorerSearchHelper.CreateLocalSnapshot), so a direct child wins even against a curated deeper row.
    public static List<SearchResultMapper.RankedCandidate> Order(List<SearchResultMapper.RankedCandidate> candidates, string? query = null)
    {
        var exact = query?.Trim();
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranked = new List<SearchResultMapper.RankedCandidate>();
        foreach (var candidate in candidates
                     // Owner's rule: history and favorites sit AFTER the match rule, not above it -- at
                     // otherwise equal match quality a row you opened before wins, but it cannot outrank a
                     // much better left-side or contiguous hit just because it is in the history.
                     // Owner's rule: a name that IS the query comes first, then match quality, and among equal
                     // matches the name order below (letters before digits before symbols, uppercase first).
                     // Owner's rule: favorites and history lead EVERYTHING, even an exact name match -- a row the
                     // user opened before is what they are looking for. Match quality orders only the rest.
                     // Owner's final ladder (2026-10-19): contiguity, left position, coverage, tier, name length,
                     // history/favorites, folder-first, natural name order. Coverage is weight/contiguity because
                     // weight is already coverage*contiguity and contiguity is carried separately.
                     .OrderByDescending(c => c.Match.Contiguity)
                     .ThenBy(c => c.Match.Start)
                     // Owner chose (A) on 2026-10-19: history and favorites outrank coverage, so a row the user
                     // opened before leads even a better-covered ordinary row. Only contiguity and position
                     // still come first.
                     .ThenByDescending(c => c.IsCurated)
                     .ThenBy(c => c.Priority)
                     .ThenByDescending(c => c.Match.Contiguity > 0 ? c.Match.Weight / c.Match.Contiguity : 0)
                     .ThenBy(c => c.Match.Tier)
                     .ThenBy(c => c.TypeRank)
                     .ThenBy(c => c.Result.Name.Length)
                     .ThenBy(c => c.Result.IsDir)
                     .ThenBy(c => c.Result.Name, NameOrderComparer.Instance))
        {
            if (usedPaths.Add(candidate.NormalizedPath))
                ranked.Add(candidate);
        }
        return ranked;
    }

    // The recency band and path depth of one index row, as the engine's sort key reads them.
    internal static (int Band, int Depth) SignalsOf(FileMetadata metadata, string path, DateTime nowUtc) =>
        (RankSignals.RecencyBand(metadata.Created, metadata.Modified, nowUtc), RankSignals.PathDepth(path));
}

// The tie-breaker the owner asked for once two rows match equally well: digits first, then letters (uppercase before
// lowercase, so "A" < "a"), digits in numeric order ("a2" < "a10") ahead of letters ("a1" < "aa"), then
internal sealed class NameOrderComparer : IComparer<string>
{
    public static readonly NameOrderComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            var cx = x[i];
            var cy = y[j];
            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                var si = i; while (i < x.Length && char.IsDigit(x[i])) i++;
                var sj = j; while (j < y.Length && char.IsDigit(y[j])) j++;
                var lx = long.Parse(x.AsSpan(si, i - si));
                var ly = long.Parse(y.AsSpan(sj, j - sj));
                if (lx != ly) return lx.CompareTo(ly);
                continue;
            }
            var kx = ClassOf(cx);
            var ky = ClassOf(cy);
            if (kx != ky) return kx.CompareTo(ky);
            if (cx != cy) return cx.CompareTo(cy);
            i++;
            j++;
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }

    private static int ClassOf(char c) => char.IsDigit(c) ? 0 : char.IsLetter(c) ? 1 : 2;
}
