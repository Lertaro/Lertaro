using Lertaro.Core.IndexV2.Alias;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.IndexV2.Search;

// The reading-grid tier of the alias fallback: a baked entry that is not an alias string but a name's
// whole reading space (AliasGridFormat), decided without enumerating that space.
//
// The shape of this tier is deliberate and narrow. It only ever produces a WITNESS -- one reading per
// position that satisfies each query term -- materializes that witness as a plain alias string, and
// then hands it to the same matching, boundary, quality-gate and weighting code the baked string
// aliases go through (FzfPattern.TryMatch / AliasMatchRules.AllowsMatch / IsAcceptableAliasMatch /
// WeightAliasMatch). So a grid row is scored, ranked and highlighted by exactly the machinery a string
// row uses, and the whole-pattern verdict (exclusions, AND across terms) is made by that machinery
// rather than re-implemented here.
//
// ponytail: the witness is built per term set independently and merged, so a multi-term query whose
// terms need conflicting readings at the same position can lose a match it would have had. Measured
// ceiling: queries with several space-separated terms over a polyphonic name. Upgrade path is a joint
// walk over (position, per-set progress) instead of one set at a time.
internal static class SearchMatcherGridAliases
{
    [ThreadStatic] private static AliasGridFormat.Index? _grid;
    [ThreadStatic] private static byte[]? _choice;
    [ThreadStatic] private static char[]? _materialized;

    /// <summary>
    /// Matches one grid entry of <paramref name="uid"/> against the query, returning the weighted result
    /// the alias tier would have produced, or null when the entry does not match.
    /// </summary>
    internal static FzfPatternResult? TryMatchEntry(Snapshot snapshot, SearchMatcher.QueryContext ctx, int entry, SearchMatcher.Worker worker)
    {
        // The AND-first `|` plus space shape is evaluated by FzfPattern against a string, which this
        // tier can only produce as one witness; rather than guess at a shape the pattern would then
        // re-interpret, leave those queries to the name and string-alias tiers.
        if (ctx.Pattern.OrGroups != null)
            return null;

        var payload = snapshot.AliasUtf8(entry);
        var grid = _grid ??= new AliasGridFormat.Index();
        if (!grid.TryBind(payload))
            return null;

        var positions = grid.PositionCount;
        if (positions == 0)
            return null;

        var separator = AliasProviderRegistry.GetSyllableSeparator(snapshot.AliasProviderId(entry));
        if (_choice == null || _choice.Length < positions)
            _choice = new byte[Math.Max(positions, 64)];
        var choice = _choice;

        FzfPatternResult? best = null;
        Span<AliasGridTier> tiers = stackalloc[] { AliasGridTier.Initials, AliasGridTier.Full };
        foreach (var tier in tiers)
        {
            if (!BuildWitness(grid, ctx, separator, tier, choice))
                continue;

            var length = AliasGridMatcher.Measure(grid, tier, choice);
            if (length == 0)
                continue;
            if (_materialized == null || _materialized.Length < length)
                _materialized = new char[Math.Max(length, 64)];

            var written = AliasGridMatcher.Materialize(grid, tier, separator, choice, _materialized);
            var text = _materialized.AsSpan(0, written);
            if (!ctx.Pattern.TryMatch(text, out var aliasMatch, FzfScoringScheme.Default, worker.Slab))
                continue;
            if (separator != '\0' && !AliasMatchRules.AllowsMatch(ctx.Pattern, separator, text, aliasMatch.MinBegin))
                continue;
            if (!IsAcceptable(ctx, aliasMatch, text, worker))
                continue;

            var weighted = ctx.Pattern.WeightAliasMatch(aliasMatch, ctx.QueryLen);
            if (best == null || weighted.Score > best.Value.Score)
                best = weighted;
        }

        return best;
    }

    // One reading choice per position satisfying every positive term set, or false when some set has no
    // witness on this tier. Positions no term constrained keep the dominant reading, which is what makes
    // the materialized string the single-reading expansion for everything the query did not pin down.
    private static bool BuildWitness(
        AliasGridFormat.Index grid,
        SearchMatcher.QueryContext ctx,
        char separator,
        AliasGridTier tier,
        byte[] choice)
    {
        Array.Clear(choice, 0, grid.PositionCount);
        foreach (var set in ctx.Pattern.EffectiveSets)
        {
            var witnessed = false;
            foreach (var term in set.Terms)
            {
                if (term.Inverse)
                    continue;
                if (AliasGridMatcher.TryMatchTerm(grid, term.Text, term.Kind, tier, separator, choice))
                {
                    witnessed = true;
                    break;
                }
            }

            if (!witnessed)
                return false;
        }
        return true;
    }

    // The same quality bar the string-alias tier applies, including its multi-term "every term lands
    // tightly" fallback for a long name (which needs the alias as chars -- available here for free).
    private static bool IsAcceptable(SearchMatcher.QueryContext ctx, FzfPatternResult aliasMatch, ReadOnlySpan<char> text, SearchMatcher.Worker worker)
        => ctx.Pattern.IsAcceptableAliasMatch(aliasMatch, ctx.QueryLen)
           || ctx.Pattern.IsAcceptableAliasMatch(aliasMatch, ctx.QueryLen, text, FzfScoringScheme.Default, worker.Slab);
}
