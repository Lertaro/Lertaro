using System.Text;
using Lertaro.Core.IndexV2.Alias;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.IndexV2.Search;

// The reading-grid tier of the alias fallback: a baked entry that is not an alias string but a name's
// whole reading space (AliasGridFormat), decided without enumerating that space.
//
// The shape of this tier is deliberate and narrow. It only ever produces a WITNESS -- one reading per
// position that satisfies each query term -- materializes that witness as a plain alias string, and
// then hands it to the same matching, boundary, quality-gate and weighting code the baked string
// aliases go through (FzfPattern.TryMatchAlias / AliasMatchRules.AllowsMatch / IsAcceptableAliasMatch /
// WeightAliasMatch). So a grid row is scored, ranked and highlighted by exactly the machinery a string
// row uses, and the whole-pattern verdict (AND across terms, quality gate) is still made there rather
// than re-implemented here. The one difference: a `/.../` clause and a `:term` describe the candidate's
// own text, so they read the name the caller handed in, not the materialized reading (see the name
// parameter below) -- the same split FzfPattern.TryMatchAlias draws.
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
    [ThreadStatic] private static AliasByteSink? _sink;

    /// <summary>
    /// Live-text overload for the tiers that hold a provider and a text rather than a baked grid entry:
    /// encodes that text's readings and decides <paramref name="pattern"/> against them, so the decision a
    /// grid-stored row gets from the snapshot is the same one a name reached through live alias generation
    /// gets (see AliasHighlightMarker and FuzzyMatcher).
    /// </summary>
    internal static bool TryMatchProvider(
        IAliasGridProvider provider,
        string text,
        FzfPattern pattern,
        FzfSlab? slab,
        bool gate,
        out FzfPatternResult match)
    {
        var sink = _sink ??= new AliasByteSink();
        sink.Reset();
        if (AliasGridFormat.Encode(text, provider, sink) <= 0)
        {
            match = default;
            return false;
        }

        return TryMatchPayload(pattern, pattern.GetTotalTermLength(), sink.Segment(0), provider.SyllableSeparator, slab, gate, out match);
    }

    /// <summary>
    /// Writes the single-reading spellings of one grid payload into <paramref name="dest"/>: the initials
    /// alias and the full-pinyin alias, which is what this provider's spelled-out aliases would have been
    /// for the same name.
    /// </summary>
    /// <remarks>
    /// For the one tier that needs an alias as TEXT mapped back onto the source rather than a verdict: the
    /// mixed-alphabet lane (a term that mixes a native-script character with alias letters, see
    /// MixedQueryMatcher) reads its alias-syntax run as a subsequence of a spelling and translates the
    /// positions it took back through <c>MapAliasToSourceIndices</c>. A grid holds every reading rather than
    /// one spelling, so the leftmost reading of each position is materialized here -- the same lossy
    /// spelling that lane has always matched, now derived from the grid instead of baked.
    /// </remarks>
    internal static bool TryMaterializeDefaults(ReadOnlySpan<byte> payload, char separator, List<string> dest)
    {
        var before = dest.Count;
        var grid = _grid ??= new AliasGridFormat.Index();
        if (!grid.TryBind(payload))
            return false;

        var positions = grid.PositionCount;
        if (positions == 0)
            return false;
        if (_choice == null || _choice.Length < positions)
            _choice = new byte[Math.Max(positions, 64)];
        Array.Clear(_choice, 0, positions);

        foreach (var tier in DefaultTiers)
        {
            var length = AliasGridMatcher.Measure(grid, tier, _choice);
            if (length == 0)
                continue;
            if (_materialized == null || _materialized.Length < length)
                _materialized = new char[Math.Max(length, 64)];

            var written = AliasGridMatcher.Materialize(grid, tier, separator, _choice, _materialized);
            dest.Add(new string(_materialized, 0, written));
        }

        return dest.Count > before;
    }

    private static readonly AliasGridTier[] DefaultTiers = { AliasGridTier.Initials, AliasGridTier.Full };

    /// <summary>
    /// Fills <paramref name="dest"/> with the alias segments the mixed-alphabet lane may match for one
    /// baked alias entry: the '|'-split spellings of a string entry, or the single-reading spellings of a
    /// grid entry (see <see cref="TryMaterializeDefaults"/>).
    /// </summary>
    internal static void CollectMixedSegments(Snapshot snapshot, int entry, byte providerId, List<string> dest)
    {
        dest.Clear();
        var aliasUtf8 = snapshot.AliasUtf8(entry);
        if (aliasUtf8.Length == 0)
            return;

        // The mixed lane matches an alias-syntax run as a subsequence of a spelling and maps the positions
        // it took back onto the source, so a grid entry has to be spelled out for it -- unlike every other
        // alias tier, which decides against the readings.
        if (snapshot.AliasKind(entry) == AliasEntryKind.Grid)
        {
            TryMaterializeDefaults(aliasUtf8, AliasProviderRegistry.GetSyllableSeparator(providerId), dest);
            return;
        }

        foreach (var segment in Encoding.UTF8.GetString(aliasUtf8).Split('|'))
        {
            if (segment.Length > 0)
                dest.Add(segment);
        }
    }

    /// <inheritdoc cref="CollectMixedSegments(Snapshot, int, byte, List{string})"/>
    /// <remarks>For the live tiers, which hold a provider and a text rather than a baked entry.</remarks>
    internal static bool TryCollectProviderSegments(IAliasGridProvider provider, string text, List<string> dest)
    {
        var sink = _sink ??= new AliasByteSink();
        sink.Reset();
        dest.Clear();
        return AliasGridFormat.Encode(text, provider, sink) > 0
               && TryMaterializeDefaults(sink.Segment(0), provider.SyllableSeparator, dest);
    }

    /// <summary>
    /// Name-search overload: applies the alias quality bar and returns the weighted result, which is the
    /// contract the baked string aliases are held to, or null when the entry does not match.
    /// <paramref name="name"/> is the candidate's own name, which the query's name-level clauses read.
    /// </summary>
    internal static FzfPatternResult? TryMatchEntry(Snapshot snapshot, SearchMatcher.QueryContext ctx, int entry, SearchMatcher.Worker worker, ReadOnlySpan<char> name)
        => TryMatchEntry(snapshot, ctx.Pattern, ctx.QueryLen, entry, worker.Slab, gate: true, name, out var match)
            ? ctx.Pattern.WeightAliasMatch(match, ctx.QueryLen)
            : null;

    /// <summary>
    /// Decides one grid entry against <paramref name="pattern"/>, writing the raw match into
    /// <paramref name="match"/>. <paramref name="gate"/> selects the alias quality bar: the name-search
    /// tier applies it, while PathGate's segment fallback deliberately does not (it keeps only the
    /// syllable-alignment rule, which is a correctness rule rather than a quality bar).
    /// <paramref name="name"/> is the text the query's name-level clauses (`/.../` and `:term`) are
    /// checked against; empty means the materialized reading is that text, which is what a path segment
    /// or an ancestor folder is asked with -- there the clause describes that very segment.
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
        ReadOnlySpan<char> name,
        out FzfPatternResult match)
        => TryMatchPayload(
            pattern,
            queryLen,
            snapshot.AliasUtf8(entry),
            AliasProviderRegistry.GetSyllableSeparator(snapshot.AliasProviderId(entry)),
            slab,
            gate,
            name,
            out match);

    /// <summary>
    /// Decides one grid payload against <paramref name="pattern"/>, writing the raw match into
    /// <paramref name="match"/>. Separate from the snapshot overload so the delta and live paths -- whose
    /// aliases are generated in memory rather than read from a snapshot -- decide through this same code,
    /// which is what keeps a name's answer the same before and after it is compacted.
    /// <paramref name="name"/> is the text the query's name-level clauses are checked against; empty
    /// means the payload's own materialized reading is that text (see the other overload).
    /// </summary>
    internal static bool TryMatchPayload(
        FzfPattern pattern,
        int queryLen,
        ReadOnlySpan<byte> payload,
        char separator,
        FzfSlab? slab,
        bool gate,
        ReadOnlySpan<char> name,
        out FzfPatternResult match)
    {
        match = default;

        // Each term witnessed as a contiguous run first and as a subsequence second, for a provider that
        // declares structure (see FzfPatternAliasReading).
        pattern = pattern.ReadingFor(separator);

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
            // The same split the string-alias tier draws: the terms read this materialized reading, while
            // a `/.../` clause and a `:term` read the candidate's own text -- `name` where the caller holds
            // one, and this same spelling where it does not, which is a path segment being asked about
            // itself (see FzfPattern.TryMatchAlias and the caller-side note in each tier).
            if (!pattern.TryMatchAlias(text, name.IsEmpty ? text : name, out var aliasMatch, FzfScoringScheme.Default, slab))
                continue;

            // No syllable-alignment check here: the witness came out of the readings themselves, and
            // AliasGridMatcher already refused every piece that would start inside one of them. Asking
            // AliasMatchRules again could only refuse a spelling the readings DO produce, because that
            // rule reads the first occurrence fzf scored rather than the one the readings chose.
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
    //
    // An AND-first query is a disjunction of AND groups, so ONE group's sets all have to witness -- not
    // every group's, which the flattened EffectiveSets would ask for.
    private static bool BuildWitness(
        AliasGridFormat.Index grid,
        FzfPattern pattern,
        char separator,
        AliasGridTier tier,
        byte[] choice)
    {
        if (pattern.OrGroups == null)
            return SetsWitness(grid, pattern.TermSets, separator, tier, choice);

        foreach (var group in pattern.OrGroups)
        {
            if (SetsWitness(grid, group.Sets, separator, tier, choice))
                return true;
        }
        return false;
    }

    private static bool SetsWitness(
        AliasGridFormat.Index grid,
        FzfTermSet[] sets,
        char separator,
        AliasGridTier tier,
        byte[] choice)
    {
        Array.Clear(choice, 0, grid.PositionCount);
        foreach (var set in sets)
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
