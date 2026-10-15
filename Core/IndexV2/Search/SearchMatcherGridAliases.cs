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
    /// Name-search overload: applies the alias quality bar and returns the weighted result, which is the
    /// contract the baked string aliases are held to, or null when the entry does not match.
    /// </summary>
    internal static FzfPatternResult? TryMatchEntry(Snapshot snapshot, SearchMatcher.QueryContext ctx, int entry, SearchMatcher.Worker worker)
        => TryMatchEntry(snapshot, ctx.Pattern, ctx.QueryLen, entry, worker.Slab, gate: true, out var match)
            ? ctx.Pattern.WeightAliasMatch(match, ctx.QueryLen)
            : null;

    /// <summary>
    /// Decides one grid entry against <paramref name="pattern"/>, writing the raw match into
    /// <paramref name="match"/>. <paramref name="gate"/> selects the alias quality bar: the name-search
    /// tier applies it, while PathGate's segment fallback deliberately does not (it keeps only the
    /// syllable-alignment rule, which is a correctness rule rather than a quality bar).
    /// </summary>
    /// <remarks>
    /// <paramref name="pattern"/> is the query itself, never an alias-side view of it: the reading is chosen
    /// here from the entry's own provider separator (see FzfPatternAliasReading.ReadingFor), so every caller
    /// passes what the user typed and the alignment rule keeps reading exactly that.
    /// </remarks>
    internal static bool TryMatchEntry(
        Snapshot snapshot,
        FzfPattern pattern,
        int queryLen,
        int entry,
        FzfSlab? slab,
        bool gate,
        out FzfPatternResult match)
        => TryMatchPayload(
            pattern,
            queryLen,
            snapshot.AliasUtf8(entry),
            AliasProviderRegistry.GetSyllableSeparator(snapshot.AliasProviderId(entry)),
            slab,
            gate,
            out match);

    /// <summary>
    /// Decides one grid payload against <paramref name="pattern"/>, writing the raw match into
    /// <paramref name="match"/>. Separate from the snapshot overload so the delta and live paths -- whose
    /// aliases are generated in memory rather than read from a snapshot -- decide through this same code,
    /// which is what keeps a name's answer the same before and after it is compacted.
    /// </summary>
    internal static bool TryMatchPayload(
        FzfPattern pattern,
        int queryLen,
        ReadOnlySpan<byte> payload,
        char separator,
        FzfSlab? slab,
        bool gate,
        out FzfPatternResult match)
    {
        match = default;

        // Each term witnessed as a contiguous run first and as a subsequence second, for a provider that
        // declares structure (see FzfPatternAliasReading).
        pattern = pattern.ReadingFor(separator);

        // The AND-first `|` plus space shape is evaluated by FzfPattern against a string, which this
        // tier can only produce as one witness; rather than guess at a shape the pattern would then
        // re-interpret, leave those queries to the name and string-alias tiers.
        if (pattern.OrGroups != null)
            return false;

        var grid = _grid ??= new AliasGridFormat.Index();
        if (!grid.TryBind(payload))
            return false;

        var positions = grid.PositionCount;
        if (positions == 0)
            return false;

        if (_choice == null || _choice.Length < positions)
            _choice = new byte[Math.Max(positions, 64)];
        var choice = _choice;

        var matched = false;
        Span<AliasGridTier> tiers = stackalloc[] { AliasGridTier.Initials, AliasGridTier.Full };
        foreach (var tier in tiers)
        {
            if (!BuildWitness(grid, pattern, separator, tier, choice))
                continue;

            var length = AliasGridMatcher.Measure(grid, tier, choice);
            if (length == 0)
                continue;
            if (_materialized == null || _materialized.Length < length)
                _materialized = new char[Math.Max(length, 64)];

            var written = AliasGridMatcher.Materialize(grid, tier, separator, choice, _materialized);
            var text = _materialized.AsSpan(0, written);
            if (!pattern.TryMatch(text, out var aliasMatch, FzfScoringScheme.Default, slab))
                continue;
            if (separator != '\0' && !AliasMatchRules.AllowsMatch(pattern, separator, text, aliasMatch.MinBegin))
                continue;
            if (gate && !IsAcceptable(pattern, queryLen, aliasMatch, text, slab))
                continue;
            if (!matched || aliasMatch.Score > match.Score)
            {
                matched = true;
                match = aliasMatch;
            }
        }

        return matched;
    }

    // One reading choice per position satisfying every positive term set, or false when some set has no
    // witness on this tier. Positions no term constrained keep the provider's first reading (index 0),
    // which is what makes the materialized string the single-reading expansion for everything the query
    // did not pin down.
    private static bool BuildWitness(
        AliasGridFormat.Index grid,
        FzfPattern pattern,
        char separator,
        AliasGridTier tier,
        byte[] choice)
    {
        Array.Clear(choice, 0, grid.PositionCount);
        foreach (var set in pattern.EffectiveSets)
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
    private static bool IsAcceptable(FzfPattern pattern, int queryLen, FzfPatternResult aliasMatch, ReadOnlySpan<char> text, FzfSlab? slab)
        => pattern.IsAcceptableAliasMatch(aliasMatch, queryLen)
           || pattern.IsAcceptableAliasMatch(aliasMatch, queryLen, text, FzfScoringScheme.Default, slab);
}
