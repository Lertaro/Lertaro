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
    //   * Match.Start           -- "left-side match priority": leftmost wins.
    //   * Match.Weight          -- then coverage*contiguity.
    //   * Match.Tier            -- then the WEAKEST key: a literal name hit beats a shorthand alias hit
    //                              beats a full transliteration (英文 > 简拼 > 全拼). Placed last so it only
    //                              separates rows that already agree on position and coverage; see
    //                              AliasMatchRules.TierFor.
    //   * path length, path     -- then the stable, deterministic tail.
    //
    // The inline window layers directory proximity ABOVE all of this separately (see
    // ExplorerSearchHelper.CreateLocalSnapshot), so a direct child wins even against a curated deeper row.
    public static List<SearchResultMapper.RankedCandidate> Order(List<SearchResultMapper.RankedCandidate> candidates)
    {
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranked = new List<SearchResultMapper.RankedCandidate>();
        foreach (var candidate in candidates
                     .OrderByDescending(c => c.IsCurated)
                     .ThenBy(c => c.Priority)
                     .ThenBy(c => c.TypeRank)
                     .ThenBy(c => c.Match.Start)
                     .ThenByDescending(c => c.Match.Weight)
                     .ThenBy(c => c.Match.Tier)
                     .ThenBy(c => c.NormalizedPath.Length)
                     .ThenBy(c => c.NormalizedPath, StringComparer.OrdinalIgnoreCase))
        {
            if (usedPaths.Add(candidate.NormalizedPath))
                ranked.Add(candidate);
        }
        return ranked;
    }
}
