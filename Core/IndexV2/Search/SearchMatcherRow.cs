using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Alias;
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

        // A `!term` has nothing to compare against in a transliteration, so the alias tiers below have to
        // be vetoed on the name -- see FzfPatternExclusionExtensions.ExclusionsPass.
        if (pattern.HasExclusions && !pattern.ExclusionsPass(name))
            return false;

        var uid = (int)snapshot.NameIds[row];
        if (!snapshot.HasAliases(uid) || snapshot.GetAliases(uid, aliasScratch) == 0)
            return false;

        var disabledIds = SearchContext.DisabledAliasIds;
        var matched = false;
        // Aliases are read the alias side's way -- each term as a run if it can be, as a subsequence
        // otherwise (see FzfPatternAliasReading) -- while the name tier above stays on `pattern`, and so does
        // the alignment rule. A provider that declares no structure has no boundary to align to, so it keeps
        // the strict reading.
        foreach (var (alias, providerId) in aliasScratch)
        {
            if (disabledIds != null && disabledIds.Contains(providerId))
                continue;
            var separator = AliasProviderRegistry.GetSyllableSeparator(providerId);
            var aliasPattern = pattern.ReadingFor(separator);
            // The alias stands in for the name only for the positive terms: an exclusion and any `/.../`
            // clause still read `name`, or they would be answered by the alias's own silence
            // (see FzfPattern.TryMatchAlias).
            if (!aliasPattern.TryMatchAlias(alias, name, out var aliasMatch, FzfScoringScheme.Default, slab))
                continue;

            // A precise query must not match a full transliteration mid-syllable -- see AliasMatchRules.
            if (!AliasMatchRules.AllowsMatch(pattern, separator, alias, aliasMatch.MinBegin))
                continue;

            if (aliasPattern.IsAcceptableAliasMatch(aliasMatch, queryLen, alias, FzfScoringScheme.Default, slab))
            {
                var weighted = aliasPattern.WeightAliasMatch(aliasMatch, queryLen);
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
                // A grid entry is spelled out for this lane, which matches an alias as text (see
                // SearchMatcherGridAliases.CollectMixedSegments).
                var segments = new List<string>(4);
                var (start, end) = snapshot.AliasEntryRange(uid);
                for (var e = start; e < end; e++)
                {
                    if (snapshot.AliasProviderId(e) != mixedTerm.ProviderId)
                        continue;

                    SearchMatcherGridAliases.CollectMixedSegments(snapshot, e, mixedTerm.ProviderId, segments);
                    foreach (var segment in segments)
                    {
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
        => TryMatchNameOrAliases(pattern, name, aliases, providerIds, Array.Empty<GridAlias>(), queryLen, slab, out result);

    // Overload for the delta/live rows, whose aliases may include reading grids (see
    // AliasGeneration.Generate). Grid entries are decided by the same code the snapshot's baked grids go
    // through, which is what keeps a row's answer the same before and after it is compacted.
    internal static bool TryMatchNameOrAliases(FzfPattern pattern, string name, string[]? aliases, byte[]? providerIds, GridAlias[] grids, int queryLen, FzfSlab slab, out FzfPatternResult result)
    {
        if (pattern.TryMatch(name, out result, FzfScoringScheme.Default, slab))
            return true;
        if (pattern.HasExclusions && !pattern.ExclusionsPass(name))
            return false;
        if (aliases == null && grids.Length == 0)
            return false;

        var disabledIds = SearchContext.DisabledAliasIds;
        var matched = false;
        aliases ??= Array.Empty<string>();
        // Same alias-side reading as MatchRow above, per entry: the name tier checked `pattern`, and every
        // alias is checked with the reading its own provider's structure allows.
        for (var j = 0; j < aliases.Length; j++)
        {
            if (disabledIds != null && providerIds != null && j < providerIds.Length && disabledIds.Contains(providerIds[j]))
                continue;
            var separator = AliasProviderRegistry.GetSyllableSeparator(providerIds?[j] ?? 0);
            var aliasPattern = pattern.ReadingFor(separator);
            // Same alias-vs-name split as MatchRow above: positives read the alias, the query's
            // name-level clauses read `name`.
            if (!aliasPattern.TryMatchAlias(aliases[j], name, out var aliasMatch, FzfScoringScheme.Default, slab))
                continue;

            // A precise query must not match a full transliteration mid-syllable -- see AliasMatchRules.
            if (!AliasMatchRules.AllowsMatch(pattern, separator, aliases[j], aliasMatch.MinBegin))
                continue;

            if (aliasPattern.IsAcceptableAliasMatch(aliasMatch, queryLen, aliases[j], FzfScoringScheme.Default, slab))
            {
                var weighted = aliasPattern.WeightAliasMatch(aliasMatch, queryLen);
                if (!matched || weighted.Score > result.Score)
                {
                    matched = true;
                    result = weighted;
                }
            }
        }

        foreach (var grid in grids)
        {
            if (disabledIds != null && disabledIds.Contains(grid.ProviderId))
                continue;
            if (!SearchMatcherGridAliases.TryMatchPayload(
                    pattern, queryLen, grid.Payload, AliasProviderRegistry.GetSyllableSeparator(grid.ProviderId), slab, gate: true, name, out var gridMatch))
                continue;

            var weightedGrid = pattern.WeightAliasMatch(gridMatch, queryLen);
            if (!matched || weightedGrid.Score > result.Score)
            {
                matched = true;
                result = weightedGrid;
            }
        }

        if (!matched && providerIds != null)
        {
            // TrySegmentPattern already excluded a disabled provider from consideration.
            var mixedTerm = MixedQueryMatcher.TrySegmentPattern(pattern);
            if (mixedTerm != null)
            {
                // This lane matches an alias as TEXT (it maps the positions it took back onto the source),
                // so a row whose reading aliases are stored as a grid has to spell them out -- see
                // SearchMatcherGridAliases.TryMaterializeDefaults.
                var segments = new List<string>(4);
                for (var j = 0; j < aliases.Length && j < providerIds.Length; j++)
                {
                    if (providerIds[j] != mixedTerm.ProviderId)
                        continue;
                    foreach (var segment in aliases[j].Split('|'))
                    {
                        if (segment.Length > 0)
                            segments.Add(segment);
                    }
                }
                foreach (var grid in grids)
                {
                    if (grid.ProviderId != mixedTerm.ProviderId)
                        continue;

                    SearchMatcherGridAliases.TryMaterializeDefaults(
                        grid.Payload, AliasProviderRegistry.GetSyllableSeparator(grid.ProviderId), segments);
                }

                foreach (var segment in segments)
                {
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

        return matched;
    }
}
