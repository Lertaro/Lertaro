using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.IndexV2.Search;

// Per-ROW / per-record matching for the small candidate sets that don't go unique-first: directory
// children listing (PathSearch.TryDirectoryChildren) and delta rows (renamed/added live updates,
// which carry their own precomputed name/alias strings). String-based on purpose -- these paths
// handle dozens of candidates, not hundreds of thousands, so the zero-decode machinery isn't worth
// threading through them.
internal static class SearchMatcherRow
{
    // Mirrors the old engine's MatchCandidate: name first, then the per-unique baked aliases with the
    // IsAcceptableAliasMatch quality gate, honoring SearchContext.DisabledAliasIds. aliasScratch is
    // caller-owned so a scan doesn't allocate a list per row.
    internal static bool MatchRow(Snapshot snapshot, int row, FzfPattern pattern, int queryLen, FzfSlab slab,
        List<(string Alias, byte ProviderId)> aliasScratch, out string name, out FzfPatternResult match)
    {
        name = snapshot.GetName(row);
        match = default;
        if (name.Length == 0)
            return false;
        if (pattern.TryMatch(name, out match, FzfScoringScheme.Default, slab))
            return true;

        var uid = (int)snapshot.NameIds[row];
        if (!snapshot.HasAliases(uid) || snapshot.GetAliases(uid, aliasScratch) == 0)
            return false;

        var disabledIds = SearchContext.DisabledAliasIds;
        var matched = false;
        foreach (var (alias, providerId) in aliasScratch)
        {
            if (disabledIds != null && disabledIds.Contains(providerId))
                continue;
            if (pattern.TryMatch(alias, out var aliasMatch, FzfScoringScheme.Default, slab)
                && pattern.IsAcceptableAliasMatch(aliasMatch, queryLen, alias, FzfScoringScheme.Default, slab))
            {
                var weighted = pattern.WeightAliasMatch(aliasMatch, queryLen);
                if (!matched || weighted.Score > match.Score)
                {
                    matched = true;
                    match = weighted;
                }
            }
        }

        if (!matched)
        {
            // TrySegmentPattern already excluded a disabled provider from consideration.
            var mixedTerm = MixedQueryMatcher.TrySegmentPattern(pattern);
            if (mixedTerm != null)
            {
                foreach (var (alias, providerId) in aliasScratch)
                {
                    if (providerId != mixedTerm.ProviderId)
                        continue;
                    foreach (var segment in alias.Split('|'))
                    {
                        if (segment.Length == 0)
                            continue;
                        if (!MixedQueryMatcher.TryMatch(mixedTerm, name.AsSpan(), name, segment, out var mm))
                            continue;
                        var candidate = new FzfPatternResult(mm.Score, mm.MinBegin, mm.MaxEnd, mm.MaxEnd, mm.ValidOffsetFound);
                        if (!matched || candidate.Score > match.Score)
                        {
                            matched = true;
                            match = candidate;
                        }
                    }
                }
            }
        }

        return matched;
    }

    // For delta rows (renamed/added), which carry their own precomputed alias array.
    internal static bool TryMatchNameOrAliases(FzfPattern pattern, string name, string[]? aliases, byte[]? providerIds, int queryLen, FzfSlab slab, out FzfPatternResult result)
    {
        if (pattern.TryMatch(name, out result, FzfScoringScheme.Default, slab))
            return true;
        if (aliases == null)
            return false;

        var disabledIds = SearchContext.DisabledAliasIds;
        var matched = false;
        for (var j = 0; j < aliases.Length; j++)
        {
            if (disabledIds != null && providerIds != null && j < providerIds.Length && disabledIds.Contains(providerIds[j]))
                continue;
            if (pattern.TryMatch(aliases[j], out var aliasMatch, FzfScoringScheme.Default, slab)
                && pattern.IsAcceptableAliasMatch(aliasMatch, queryLen, aliases[j], FzfScoringScheme.Default, slab))
            {
                var weighted = pattern.WeightAliasMatch(aliasMatch, queryLen);
                if (!matched || weighted.Score > result.Score)
                {
                    matched = true;
                    result = weighted;
                }
            }
        }

        if (!matched && providerIds != null)
        {
            // TrySegmentPattern already excluded a disabled provider from consideration.
            var mixedTerm = MixedQueryMatcher.TrySegmentPattern(pattern);
            if (mixedTerm != null)
            {
                for (var j = 0; j < aliases.Length && j < providerIds.Length; j++)
                {
                    if (providerIds[j] != mixedTerm.ProviderId)
                        continue;
                    foreach (var segment in aliases[j].Split('|'))
                    {
                        if (segment.Length == 0)
                            continue;
                        if (!MixedQueryMatcher.TryMatch(mixedTerm, name.AsSpan(), name, segment, out var mm))
                            continue;
                        var candidate = new FzfPatternResult(mm.Score, mm.MinBegin, mm.MaxEnd, mm.MaxEnd, mm.ValidOffsetFound);
                        if (!matched || candidate.Score > result.Score)
                        {
                            matched = true;
                            result = candidate;
                        }
                    }
                }
            }
        }

        return matched;
    }
}
