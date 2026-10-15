using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.IndexV2.Alias;

// Which of a provider's two alias tiers a grid term matched on. Kept apart because the caller feeds
// them through the same pipeline the baked string aliases go through, one materialized string each --
// so a grid hit is scored, ranked and highlighted by exactly the code that handles a string alias.
internal enum AliasGridTier
{
    Initials,
    Full,
}

// Where a contiguous (non-fuzzy) match is allowed to begin and end. FzfTermKind's precise operators all
// mean "the term appears as one unbroken run"; they differ only in which end of the alias that run is
// pinned to.
internal enum AliasGridAnchor
{
    Any,
    Boundary,
    AliasStart,
    AliasEnd,
    Whole,
}

/// <summary>
/// Decides a parsed query term against a name's reading grid, and materializes the reading choice a
/// match relies on. This is the half of the reading grid that replaces enumeration: the grid holds
/// every spelling implicitly, so a term is tested by walking it over the positions instead of testing
/// it against each of the k^n spellings.
/// </summary>
/// <remarks>
/// Both sides are lowercase by contract -- alias readings are lowercase by
/// <see cref="Lertaro.PluginSdk.Abstractions.Plugins.IAliasProvider"/> and the parser lowercases terms --
/// so comparison is ordinal and the case-sensitivity flag has nothing left to do.
///
/// A match here is a <em>witness</em>, not a verdict. The caller materializes the chosen readings and
/// runs the real matcher (fzf scoring, the syllable-boundary rule, the alias quality gate) over that
/// string, and that pipeline decides. A witness that does not survive it means this row is not reported,
/// never a false hit.
/// </remarks>
internal static class AliasGridMatcher
{
    // Ceiling for the contiguous walk: a pathological name whose readings all share a first character
    // could otherwise branch at every position. A precise-operator query over such a row then simply
    // reports no alias match, which costs recall only for `^`/`$`/`=` style terms.
    // ponytail: a step budget, not a memoized automaton; the upgrade path is the fuzzy path's DP with
    // contiguity carried as a second state bit.
    private const int ExactStepBudget = 8192;

    // The DP table is (positions + 1) * (term + 1) cells. A term long enough to push this past the cap
    // against a long name gets no answer rather than a multi-megabyte allocation whose result would
    // almost always be "no match".
    private const int MaxDpCells = 1 << 18;

    [ThreadStatic] private static int[]? _prevJ;
    [ThreadStatic] private static byte[]? _choiceReading;
    [ThreadStatic] private static bool[]? _reachable;
    [ThreadStatic] private static bool[]? _next;
    [ThreadStatic] private static byte[]? _draft;

    /// <summary>
    /// Decides <paramref name="term"/> (one alternative spelling of one AND condition) against
    /// <paramref name="grid"/> on <paramref name="tier"/>. On true, <paramref name="choice"/> holds one
    /// reading index per position, with the positions the term did not pin down left at whatever the
    /// caller had there -- so a caller that pre-fills it with the provider's first reading (index 0) gets
    /// the single-reading expansion for everything the query did not constrain.
    /// </summary>
    /// <remarks>On false the contents of <paramref name="choice"/> are undefined.</remarks>
    public static bool TryMatchTerm(
        AliasGridFormat.Index grid,
        ReadOnlySpan<char> term,
        FzfTermKind kind,
        AliasGridTier tier,
        char separator,
        Span<byte> choice)
    {
        var positions = grid.PositionCount;
        if (term.IsEmpty || positions == 0 || choice.Length < positions)
            return false;

        var anchor = AnchorFor(kind);
        if (anchor is null)
            return false;

        var draft = _draft;
        if (draft == null || draft.Length < positions)
            _draft = draft = new byte[Math.Max(positions, 64)];
        choice[..positions].CopyTo(draft);

        var matched = (kind, tier) switch
        {
            (FzfTermKind.Fuzzy, AliasGridTier.Initials) => MatchFuzzyInitials(grid, term, draft),
            (FzfTermKind.Fuzzy, _) => MatchFuzzyFull(grid, term, separator, draft),
            (_, AliasGridTier.Initials) => MatchExactInitials(grid, term, anchor.Value, draft),
            _ => MatchExactFull(grid, term, separator, anchor.Value, draft),
        };

        if (!matched)
            return false;

        draft.AsSpan(0, positions).CopyTo(choice);
        return true;
    }

    private static AliasGridAnchor? AnchorFor(FzfTermKind kind) => kind switch
    {
        FzfTermKind.Fuzzy or FzfTermKind.Exact => AliasGridAnchor.Any,
        FzfTermKind.ExactBoundary => AliasGridAnchor.Boundary,
        FzfTermKind.Prefix => AliasGridAnchor.AliasStart,
        FzfTermKind.Suffix => AliasGridAnchor.AliasEnd,
        FzfTermKind.Equal => AliasGridAnchor.Whole,
        _ => null,
    };

    // One character per position, so a fuzzy term is satisfied exactly when each of its characters can
    // be taken from a later position than the previous one. Taking the earliest position that can serve
    // each character is always the best choice, which makes one left-to-right pass both necessary and
    // sufficient.
    private static bool MatchFuzzyInitials(AliasGridFormat.Index grid, ReadOnlySpan<char> term, byte[] choice)
    {
        var qi = 0;
        for (var p = 0; p < grid.PositionCount && qi < term.Length; p++)
        {
            var count = grid.ReadingCount(p);
            for (var k = 0; k < count; k++)
            {
                var reading = grid.Reading(p, k);
                if (reading.Length > 0 && reading[0] == term[qi])
                {
                    choice[p] = (byte)k;
                    qi++;
                    break;
                }
            }
        }
        return qi == term.Length;
    }

    // The full tier emits a whole syllable per position, and a query character that ends one syllable
    // may be followed by more of that same syllable, so which reading is chosen at a position decides
    // how much of the term that position can absorb. That is a real search: reachable[j] after the first
    // p positions means "the first j characters of the term can be produced by some choice of readings
    // over those positions", and each position extends it by consuming a greedy run.
    private static bool MatchFuzzyFull(AliasGridFormat.Index grid, ReadOnlySpan<char> term, char separator, byte[] choice)
    {
        var positions = grid.PositionCount;
        var length = term.Length;
        var stride = length + 1;
        if ((long)(positions + 1) * stride > MaxDpCells)
            return false;

        EnsureDp(positions + 1, stride);
        var reachable = _reachable!;
        var next = _next!;
        var prevJ = _prevJ!;
        var choiceReading = _choiceReading!;

        Array.Clear(reachable, 0, stride);
        reachable[0] = true;

        for (var p = 0; p < positions; p++)
        {
            Array.Clear(next, 0, stride);
            var count = grid.ReadingCount(p);
            for (var j = 0; j <= length; j++)
            {
                if (!reachable[j])
                    continue;

                for (var k = 0; k < count; k++)
                {
                    var consumed = Consume(grid, p, k, separator, term, j);
                    if (next[consumed])
                        continue;

                    next[consumed] = true;
                    prevJ[(p + 1) * stride + consumed] = j;
                    choiceReading[(p + 1) * stride + consumed] = (byte)k;
                }
            }

            (reachable, next) = (next, reachable);
        }

        if (!reachable[length])
            return false;

        var cursor = length;
        for (var p = positions; p >= 1; p--)
        {
            choice[p - 1] = choiceReading[p * stride + cursor];
            cursor = prevJ[p * stride + cursor];
        }
        return true;
    }

    // Furthest term position reachable by taking this position's reading as a subsequence, plus its
    // separator when the boundary bit says one is emitted and the term expects it there.
    private static int Consume(AliasGridFormat.Index grid, int position, int reading, char separator, ReadOnlySpan<char> term, int from)
    {
        var j = from;
        if (grid.BoundaryBefore(position) && j < term.Length && term[j] == separator)
            j++;

        var text = grid.Reading(position, reading);
        for (var u = 0; u < text.Length && j < term.Length; u++)
        {
            if (text[u] == term[j])
                j++;
        }
        return j;
    }

    // Contiguous run over one-character-per-position readings: the run occupies consecutive positions,
    // pinned to the alias end when the operator says so, and starts at a position boundary.
    private static bool MatchExactInitials(AliasGridFormat.Index grid, ReadOnlySpan<char> term, AliasGridAnchor anchor, byte[] choice)
    {
        var positions = grid.PositionCount;
        if (term.Length > positions)
            return false;

        var start = anchor == AliasGridAnchor.AliasEnd ? positions - term.Length : 0;
        for (; start + term.Length <= positions; start++)
        {
            if (anchor is AliasGridAnchor.AliasStart or AliasGridAnchor.Whole && start != 0)
                break;

            var ok = true;
            for (var t = 0; t < term.Length && ok; t++)
            {
                var count = grid.ReadingCount(start + t);
                var found = -1;
                for (var k = 0; k < count; k++)
                {
                    var reading = grid.Reading(start + t, k);
                    if (reading.Length > 0 && reading[0] == term[t])
                    {
                        found = k;
                        break;
                    }
                }

                if (found < 0)
                    ok = false;
                else
                    choice[start + t] = (byte)found;
            }

            if (!ok)
                continue;
            if (anchor == AliasGridAnchor.AliasEnd && start + term.Length != positions)
                continue;
            return true;
        }
        return false;
    }

    // Contiguous run over whole readings. A reading is several characters long, so the run may begin and
    // end inside one, and may cross position boundaries -- where the next position's reading has to
    // continue the term from its own first emitted character.
    private static bool MatchExactFull(AliasGridFormat.Index grid, ReadOnlySpan<char> term, char separator, AliasGridAnchor anchor, byte[] choice)
    {
        var budget = ExactStepBudget;
        for (var p = 0; p < grid.PositionCount; p++)
        {
            var count = grid.ReadingCount(p);
            for (var k = 0; k < count; k++)
            {
                var emitted = EmittedLength(grid, p, k);
                for (var offset = 0; offset < emitted; offset++)
                {
                    if (!StartAllowed(grid, p, offset, anchor))
                        continue;
                    if (WalkExact(grid, p, k, offset, term, separator, anchor, choice, ref budget))
                        return true;
                }
            }
        }
        return false;
    }

    private static bool StartAllowed(AliasGridFormat.Index grid, int position, int offset, AliasGridAnchor anchor) => anchor switch
    {
        AliasGridAnchor.AliasStart or AliasGridAnchor.Whole => position == 0 && offset == 0,
        AliasGridAnchor.Boundary => offset == 0 && (position == 0 || grid.BoundaryBefore(position)),
        _ => true,
    };

    // Consumes the term from (position, reading, offset) outwards, one emitted character at a time. At a
    // reading boundary the next reading must supply the term's next character as its own first emitted
    // character, so only readings that can do so are explored.
    private static bool WalkExact(
        AliasGridFormat.Index grid,
        int position,
        int reading,
        int offset,
        ReadOnlySpan<char> term,
        char separator,
        AliasGridAnchor anchor,
        byte[] choice,
        ref int budget)
    {
        var t = 0;
        var p = position;
        var k = reading;
        var o = offset;
        choice[p] = (byte)k;

        while (true)
        {
            if (--budget <= 0)
                return false;

            var text = grid.Reading(p, k);
            var hasSeparator = grid.BoundaryBefore(p);
            var emitted = text.Length + (hasSeparator ? 1 : 0);
            while (o < emitted && t < term.Length)
            {
                var c = hasSeparator && o == 0 ? separator : text[o - (hasSeparator ? 1 : 0)];
                if (c != term[t])
                    break;
                t++;
                o++;
            }

            if (t == term.Length)
            {
                if (anchor is AliasGridAnchor.AliasEnd or AliasGridAnchor.Whole
                    && (p != grid.PositionCount - 1 || o != EmittedLength(grid, p, k)))
                    return false;
                return true;
            }

            // A mismatch inside a reading that still had characters left breaks contiguity, and the run
            // cannot resume past it: the alias has other characters in between.
            if (o < emitted)
                return false;

            p++;
            if (p >= grid.PositionCount)
                return false;

            var count = grid.ReadingCount(p);
            var continued = false;
            for (var candidate = 0; candidate < count; candidate++)
            {
                if (FirstEmitted(grid, p, candidate, separator) != term[t])
                    continue;
                choice[p] = (byte)candidate;
                k = candidate;
                o = 0;
                continued = true;
                break;
            }

            if (!continued)
                return false;
        }
    }

    private static char FirstEmitted(AliasGridFormat.Index grid, int position, int reading, char separator)
    {
        if (grid.BoundaryBefore(position))
            return separator;
        var text = grid.Reading(position, reading);
        return text.Length > 0 ? text[0] : '\0';
    }

    private static int EmittedLength(AliasGridFormat.Index grid, int position, int reading)
        => grid.Reading(position, reading).Length + (grid.BoundaryBefore(position) ? 1 : 0);

    /// <summary>
    /// Length of the alias string <paramref name="choice"/> spells out on <paramref name="tier"/>,
    /// measured without materializing it.
    /// </summary>
    public static int Measure(AliasGridFormat.Index grid, AliasGridTier tier, ReadOnlySpan<byte> choice)
    {
        var total = 0;
        for (var p = 0; p < grid.PositionCount; p++)
        {
            if (tier == AliasGridTier.Full && grid.BoundaryBefore(p))
                total++;
            total += tier == AliasGridTier.Initials ? 1 : grid.Reading(p, choice[p]).Length;
        }
        return total;
    }

    /// <summary>
    /// Writes the alias string <paramref name="choice"/> spells out, into <paramref name="dest"/> sized
    /// from <see cref="Measure"/>. Returns the written length.
    /// </summary>
    public static int Materialize(AliasGridFormat.Index grid, AliasGridTier tier, char separator, ReadOnlySpan<byte> choice, Span<char> dest)
    {
        var written = 0;
        for (var p = 0; p < grid.PositionCount; p++)
        {
            if (tier == AliasGridTier.Full && grid.BoundaryBefore(p))
                dest[written++] = separator;

            var text = grid.Reading(p, choice[p]);
            if (tier == AliasGridTier.Initials)
                dest[written++] = text[0];
            else
            {
                text.CopyTo(dest[written..]);
                written += text.Length;
            }
        }
        return written;
    }

    private static void EnsureDp(int positions, int stride)
    {
        var cells = positions * stride;
        if (_prevJ == null || _prevJ.Length < cells)
        {
            _prevJ = new int[cells];
            _choiceReading = new byte[cells];
        }

        if (_reachable == null || _reachable.Length < stride)
        {
            _reachable = new bool[stride];
            _next = new bool[stride];
        }
    }
}
