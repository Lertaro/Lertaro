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
// position that satisfies each query term -- materializes that witness as a plain alias, and then hands
// it to the same matching and weighting code the baked string aliases go through (FzfPattern.TryMatch /
// WeightAliasMatch, on the byte engine when the spelling is pure ASCII, which every pinyin spelling is).
// So a grid row is scored, ranked and highlighted by exactly the machinery a string row uses, and the
// whole-pattern verdict (exclusions, AND across terms) is made by that machinery rather than
// re-implemented here. It judges the alias quality bar on the witness's own evidence: a term that spells a
// reading out is not bound by the span limit (the text between its pieces is the name's own syllables),
// while a term of pure initials keeps the shared bar -- see IsAcceptableGridWitness. It keeps the
// alignment rule out entirely: the witness already came out of the readings.
//
// ponytail: the first witness is built per term set and merged, so a multi-term query whose sets need
// conflicting readings of one character loses that merge -- the retry (AliasGridJointMatcher) answers it
// with one choice for all sets, but declines a query holding a precise operator, a set whose alternatives
// are not the same letters, and shapes past its cell budget; those keep the merge's ceiling. Upgrade path
// is that same joint walk covering those shapes too, instead of a second strategy per tier.
internal static class SearchMatcherGridAliases
{
    [ThreadStatic] private static AliasGridFormat.Index? _grid;
    [ThreadStatic] private static byte[]? _choice;
    [ThreadStatic] private static int[]? _painted;
    [ThreadStatic] private static char[]? _materialized;
    [ThreadStatic] private static byte[]? _aliasBytes;
    [ThreadStatic] private static FzfByteBuffers? _byteBuffers;
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
    internal static bool TryMaterializeDefaults(ReadOnlySpan<byte> payload, AliasReadingTable? readings, char separator, List<string> dest)
    {
        var before = dest.Count;
        var grid = _grid ??= new AliasGridFormat.Index();
        if (!grid.TryBind(payload, readings))
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
            TryMaterializeDefaults(aliasUtf8, snapshot.AliasReadings, AliasProviderRegistry.GetSyllableSeparator(providerId), dest);
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
               && TryMaterializeDefaults(sink.Segment(0), null, provider.SyllableSeparator, dest);
    }

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
            snapshot.AliasReadings,
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
        => TryMatchPayload(pattern, queryLen, payload, null, separator, slab, gate, out match);

    /// <inheritdoc cref="TryMatchPayload(FzfPattern, int, ReadOnlySpan{byte}, char, FzfSlab?, bool, out FzfPatternResult)"/>
    /// <param name="readings">
    /// The snapshot's reading vocabulary, for a payload that references its readings by id (see
    /// AliasReadingTable). Null for the self-contained shape the delta overlay and the live tiers write.
    /// </param>
    internal static bool TryMatchPayload(
        FzfPattern pattern,
        int queryLen,
        ReadOnlySpan<byte> payload,
        AliasReadingTable? readings,
        char separator,
        FzfSlab? slab,
        bool gate,
        out FzfPatternResult match)
    {
        match = default;

        // Each term witnessed as a contiguous run first and as a subsequence second, for a provider that
        // declares structure (see FzfPatternAliasReading).
        pattern = pattern.ReadingFor(separator);

        var grid = _grid ??= new AliasGridFormat.Index();
        if (!grid.TryBind(payload, readings))
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
            // The per-set merge first: it is the cheap walk and it answers almost everything. A set that
            // witnesses nowhere cannot witness inside a shared choice either, so a tier that refuses here
            // refuses for good.
            if (!BuildWitness(grid, pattern, separator, tier, choice, out var spellsAReading))
                continue;

            var outcome = TryScoreWitness(grid, tier, separator, choice, pattern, queryLen, slab, gate, spellsAReading, out var scored);

            // The sets witnessed, and the one spelling they merged into matches none of them: they needed
            // different readings of the same character, and whichever set was merged last decided what that
            // spelling said. One choice for all sets at once is the witness the merge cannot produce. Only
            // the matcher's own refusal earns that walk -- a spelling the quality bar refused is refused
            // whoever produced it, and this is the hot tail of every scan.
            if (outcome == WitnessScore.NoMatch
                && JointWitness(grid, pattern, separator, tier, choice, out var jointSpelling)
                && TryScoreWitness(grid, tier, separator, choice, pattern, queryLen, slab, gate, jointSpelling, out scored) == WitnessScore.Accepted)
                outcome = WitnessScore.Accepted;

            if (outcome != WitnessScore.Accepted)
                continue;

            if (!matched || scored.Score > match.Score)
            {
                matched = true;
                match = scored;
            }
        }

        return matched;
    }

    // Whether a witness's spelling decided the row: matched and allowed, matched and refused by the quality
    // bar, or not matched at all. The third is the only one a second witness can help with.
    private enum WitnessScore
    {
        Accepted,
        RefusedByBar,
        NoMatch,
    }

    // Materializes the witness this tier's reading choice made and hands it to the matcher, the quality bar
    // and the weighting -- the same pipeline a baked string alias goes through, so the whole-pattern verdict
    // (exclusions, AND across terms) is made there rather than re-implemented here.
    private static WitnessScore TryScoreWitness(
        AliasGridFormat.Index grid,
        AliasGridTier tier,
        char separator,
        byte[] choice,
        FzfPattern pattern,
        int queryLen,
        FzfSlab? slab,
        bool gate,
        bool spellsAReading,
        out FzfPatternResult match)
    {
        match = default;

        var length = AliasGridMatcher.Measure(grid, tier, choice);
        if (length == 0)
            return WitnessScore.NoMatch;

        // The reading walk above already decided the match; what runs here only scores the spelling that
        // decision chose. A pinyin spelling is ASCII, and for ASCII the byte engine answers the same match at
        // the same offsets with nothing to decode (see FzfByteMatcher), so materializing straight into bytes
        // replaces "build a char array, then run the char engine over it" with one pass. An empty alias below
        // is a shape verdict, not a miss: the spelling holds a code unit that is not ASCII -- a character the
        // provider passed through untransliterated -- so the char lane stays the one that decides it. A
        // caller with no slab to score against has nowhere to match bytes either, and takes that same lane.
        if (slab is { } scoringSlab)
        {
            var byteAlias = TryMaterializeBytes(grid, tier, separator, choice, length);
            if (!byteAlias.IsEmpty)
            {
                // No syllable-alignment check here: the witness came out of the readings themselves, and
                // AliasGridMatcher already refused every piece that would start inside one of them. Asking
                // AliasMatchRules again could only refuse a spelling the readings DO produce, because that
                // rule reads the first occurrence fzf scored rather than the one the readings chose.
                if (!pattern.ByteView.TryMatchSegmented(byteAlias, out var byteMatch, FzfScoringScheme.Default, scoringSlab, _byteBuffers ??= new FzfByteBuffers()))
                    return WitnessScore.NoMatch;
                if (gate && !IsAcceptableWitness(pattern, byteMatch, queryLen, spellsAReading, byteAlias, scoringSlab))
                    return WitnessScore.RefusedByBar;

                match = byteMatch;
                return WitnessScore.Accepted;
            }
        }

        if (_materialized == null || _materialized.Length < length)
            _materialized = new char[Math.Max(length, 64)];

        var chars = AliasGridMatcher.Materialize(grid, tier, separator, choice, _materialized);
        var text = _materialized.AsSpan(0, chars);
        if (!pattern.TryMatch(text, out var aliasMatch, FzfScoringScheme.Default, slab))
            return WitnessScore.NoMatch;
        if (gate && !IsAcceptableWitness(pattern, aliasMatch, queryLen, spellsAReading, text, slab))
            return WitnessScore.RefusedByBar;

        match = aliasMatch;
        return WitnessScore.Accepted;
    }

    // The same question one step further: ONE reading choice that satisfies every set at once, for the
    // queries whose sets need different readings of the same character. See AliasGridJointMatcher, which also
    // reports the spelling flag the merge computes from its own walks.
    private static bool JointWitness(
        AliasGridFormat.Index grid,
        FzfPattern pattern,
        char separator,
        AliasGridTier tier,
        byte[] choice,
        out bool spellsAReading)
    {
        // The merge left its own choice behind; this walk answers from the provider's first readings.
        Array.Clear(choice, 0, grid.PositionCount);

        spellsAReading = false;
        if (pattern.OrGroups == null)
            return AliasGridJointMatcher.TryMatchSets(grid, pattern.TermSets, separator, tier, choice, out spellsAReading);

        foreach (var group in pattern.OrGroups)
        {
            if (AliasGridJointMatcher.TryMatchSets(grid, group.Sets, separator, tier, choice, out spellsAReading))
                return true;
        }
        return false;
    }

    // One reading choice per position satisfying every positive term set, or false when some set has no
    // witness on this tier. Positions no term constrained keep the provider's first reading (index 0),
    // which is what makes the materialized string the single-reading expansion for everything the query
    // did not pin down.
    //
    // An AND-first query is a disjunction of AND groups, so ONE group's sets all have to witness -- not
    // every group's, which the flattened EffectiveSets would ask for.
    //
    // spellsAReading comes out with it: true when some term of the winning group took fewer positions than
    // it has letters, i.e. spelled a reading out rather than abbreviating every character of it. The alias
    // quality bar weighs those two differently -- see FzfPatternAliasMatchExtensions.IsAcceptableGridWitness.
    private static bool BuildWitness(
        AliasGridFormat.Index grid,
        FzfPattern pattern,
        char separator,
        AliasGridTier tier,
        byte[] choice,
        out bool spellsAReading)
    {
        spellsAReading = false;
        if (pattern.OrGroups == null)
            return SetsWitness(grid, pattern.TermSets, separator, tier, choice, ref spellsAReading);

        foreach (var group in pattern.OrGroups)
        {
            var reading = false;
            if (!SetsWitness(grid, group.Sets, separator, tier, choice, ref reading))
                continue;

            spellsAReading = reading;
            return true;
        }
        return false;
    }

    private static bool SetsWitness(
        AliasGridFormat.Index grid,
        FzfTermSet[] sets,
        char separator,
        AliasGridTier tier,
        byte[] choice,
        ref bool spellsAReading)
    {
        Array.Clear(choice, 0, grid.PositionCount);
        if (_painted == null || _painted.Length < grid.PositionCount)
            _painted = new int[Math.Max(grid.PositionCount, 64)];
        var painted = _painted;
        foreach (var set in sets)
        {
            var witnessed = false;
            foreach (var term in set.Terms)
            {
                if (term.Inverse)
                    continue;
                if (!AliasGridMatcher.TryMatchTerm(grid, term.Text, term.Kind, tier, separator, choice, painted, out var paintedCount))
                    continue;

                witnessed = true;
                // One position per letter is an abbreviation; fewer than that means a position gave up more
                // than its initial, which only the readings themselves can authorize.
                if (paintedCount < term.Text.Length)
                    spellsAReading = true;
                break;
            }

            if (!witnessed)
                return false;
        }
        return true;
    }

    // The witness's bar, plus the one tail the abbreviation case still needs the shared bar's per-term
    // tight-window fallback for. A spelling the readings authorized is judged on its score alone.
    private static bool IsAcceptableWitness(
        FzfPattern pattern,
        FzfPatternResult match,
        int queryLen,
        bool spellsAReading,
        ReadOnlySpan<char> alias,
        FzfSlab? slab)
        => pattern.IsAcceptableGridWitness(match, queryLen, spellsAReading)
           || !spellsAReading && pattern.IsAcceptableAliasMatch(match, queryLen, alias, FzfScoringScheme.Default, slab);

    // Byte twin: the alias is widened to chars only once the cheap check has already failed and the term is
    // a bare abbreviation -- the same deferral the string tier applies to its UTF-8 decode.
    private static bool IsAcceptableWitness(
        FzfPattern pattern,
        FzfPatternResult match,
        int queryLen,
        bool spellsAReading,
        ReadOnlySpan<byte> alias,
        FzfSlab slab)
        => pattern.IsAcceptableGridWitness(match, queryLen, spellsAReading)
           || !spellsAReading && pattern.IsAcceptableAliasMatch(match, queryLen, WidenToChars(alias), FzfScoringScheme.Default, slab);

    // Materializes one tier's spelling into the thread's byte scratch, sized from the measured length. An
    // empty span means the spelling is not ASCII-representable, which is unambiguous because a measured
    // length of at least one always writes at least one byte.
    private static ReadOnlySpan<byte> TryMaterializeBytes(AliasGridFormat.Index grid, AliasGridTier tier, char separator, byte[] choice, int length)
    {
        if (_aliasBytes == null || _aliasBytes.Length < length)
            _aliasBytes = new byte[Math.Max(length, 64)];

        var written = AliasGridMatcher.TryMaterializeBytes(grid, tier, separator, choice, _aliasBytes);
        return written < 0 ? default : _aliasBytes.AsSpan(0, written);
    }

    // Below 0x80 a byte IS its code unit, so widening is a copy, not a decode. Only the abbreviation case
    // needs it: the per-term tight-window fallback of the shared alias bar reads the alias as text.
    private static ReadOnlySpan<char> WidenToChars(ReadOnlySpan<byte> alias)
    {
        if (_materialized == null || _materialized.Length < alias.Length)
            _materialized = new char[Math.Max(alias.Length, 64)];

        for (var i = 0; i < alias.Length; i++)
            _materialized[i] = (char)alias[i];
        return _materialized.AsSpan(0, alias.Length);
    }
}
