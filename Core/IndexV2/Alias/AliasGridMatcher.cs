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
/// A plain term (FzfTermKind.Exact or Fuzzy) is decided by the same reading-by-reading rule on both tiers
/// (see MatchSyllableFull), which is why the two matching modes answer the same thing here: a contiguous
/// run is one shape of a spelling that rule describes, so the rule covers what the strict reading used to
/// add, and the loose reading used to add more than any spelling of these characters. The anchored
/// operator kinds (`^`, `$`, `=`, `'x'`) keep their own contiguous, pinned walk -- an operator says exactly
/// what it means.
///
/// A match here is a <em>witness</em>, not a verdict. The caller materializes the chosen readings and
/// runs the real matcher (fzf scoring, the alias quality gate) over that string, and that pipeline
/// decides. A witness that does not survive it means this row is not reported, never a false hit.
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
    [ThreadStatic] private static char[]? _stripped;

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
        => TryMatchTerm(grid, term, kind, tier, separator, choice, default, out _);

    /// <summary>
    /// Decides <paramref name="term"/> (one alternative spelling of one AND condition) against
    /// <paramref name="grid"/> on <paramref name="tier"/>. On true, <paramref name="choice"/> holds one
    /// reading index per position, with the positions the term did not pin down left at whatever the
    /// caller had there -- so a caller that pre-fills it with the provider's first reading (index 0) gets
    /// the single-reading expansion for everything the query did not constrain.
    /// </summary>
    /// <param name="painted">
    /// Optional witness for the paint step: the positions whose readings the term actually consumed, written
    /// from index 0 and counted by <paramref name="paintedCount"/>. A position appears at most once because
    /// a position contributes at most one piece, and the grid has one position per code unit of the text it
    /// was encoded from -- so these are directly the characters to light up. That is the only honest source:
    /// re-searching the materialized alias can align the term onto a DIFFERENT reading choice than the one
    /// that decided the match, and light syllables this term never spelled.
    /// </param>
    public static bool TryMatchTerm(
        AliasGridFormat.Index grid,
        ReadOnlySpan<char> term,
        FzfTermKind kind,
        AliasGridTier tier,
        char separator,
        Span<byte> choice,
        Span<int> painted,
        out int paintedCount)
    {
        paintedCount = 0;
        var positions = grid.PositionCount;
        if (term.IsEmpty || positions == 0 || choice.Length < positions)
            return false;

        var draft = _draft;
        if (draft == null || draft.Length < positions)
            _draft = draft = new byte[Math.Max(positions, 64)];
        choice[..positions].CopyTo(draft);

        bool matched;
        if (kind is FzfTermKind.Fuzzy or FzfTermKind.Exact)
        {
            // Both modes take the same reading of a plain term: see this class's header and MatchSyllableFull.
            // There is no "this is a precise query" special case left on the grid.
            //
            // The provider's separator marks where its own query segmenter believed a boundary fell; which
            // part of a reading a piece may cover is decided from the readings here, so only the term's
            // letters matter. A query form the provider emitted for a run of letters ("zheng<SEP>shu") is
            // therefore read as the same term the user typed.
            term = StripSeparator(term, separator);
            if (term.IsEmpty)
                return false;

            matched = tier == AliasGridTier.Initials
                ? MatchInitialsSubsequence(grid, term, draft, painted, ref paintedCount)
                : MatchSyllableFull(grid, term, draft, painted, ref paintedCount);
        }
        else
        {
            var anchor = AnchorFor(kind);
            if (anchor is null)
                return false;

            // An operator's term keeps its separator: it names a character of the ALIAS, and the alias is
            // what spells that character.
            matched = tier == AliasGridTier.Initials
                ? MatchExactInitials(grid, term, anchor.Value, draft, painted, ref paintedCount)
                : MatchExactFull(grid, term, separator, anchor.Value, draft, painted, ref paintedCount);
        }

        if (!matched)
        {
            paintedCount = 0;
            return false;
        }

        draft.AsSpan(0, positions).CopyTo(choice);
        return true;
    }

    private static AliasGridAnchor? AnchorFor(FzfTermKind kind) => kind switch
    {
        FzfTermKind.ExactBoundary => AliasGridAnchor.Boundary,
        FzfTermKind.Prefix => AliasGridAnchor.AliasStart,
        FzfTermKind.Suffix => AliasGridAnchor.AliasEnd,
        FzfTermKind.Equal => AliasGridAnchor.Whole,
        _ => null,
    };

    // One character per position, so a term is satisfied exactly when each of its characters can be
    // taken from a later position than the previous one. Taking the earliest position that can serve
    // each character is always the best choice, which makes one left-to-right pass both necessary and
    // sufficient. Every position of the initials tier is a whole reading of its own, so this IS the rule
    // MatchSyllableFull implements, restricted to one-character pieces: each piece starts where its reading
    // starts, and skipping a character is always allowed.
    private static bool MatchInitialsSubsequence(AliasGridFormat.Index grid, ReadOnlySpan<char> term, byte[] choice, Span<int> painted, ref int paintedCount)
    {
        // Every character of a term on this tier IS some position's reading initial, so one bitmap answers
        // for all of them before a position is walked. Most candidates are refused here, not in the walk.
        if (!grid.StartsFromAll(term))
            return false;

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
                    if (paintedCount < painted.Length)
                        painted[paintedCount++] = p;
                    qi++;
                    break;
                }
            }
        }
        return qi == term.Length;
    }

    // The full tier emits a whole reading per position, and a piece of the term is a PREFIX of one
    // reading: the whole reading, its first letter, or -- only where the term ends -- a half-typed start.
    // A position contributes at most one piece, which is what makes "piji" (pi from ping, ji from jia)
    // unreachable while "pingj" and "pjia" are: leaving a reading before its end is only ever the cursor
    // sitting there, never a way to keep consuming the next character.
    //
    // That is a reachability walk, not a greedy one: which reading is chosen at a position decides how
    // much of the term it can absorb, so reachable[j] after the first p positions means "some choice of
    // readings over those positions spells the first j characters", and each position extends it by the
    // pieces it can form.
    private static bool MatchSyllableFull(AliasGridFormat.Index grid, ReadOnlySpan<char> term, byte[] choice, Span<int> painted, ref int paintedCount)
    {
        // The term opens with the first character of its first piece, and a piece starts where its reading
        // does -- so a name no syllable of which can start with that letter is refused without a walk. This
        // is where most candidates of a scan actually drop out.
        if (!grid.StartsFrom(term[0]))
            return false;

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

        // How many positions the walk actually consumed. A term is spelled by the time reachable[length]
        // is set, and every later position can only CARRY that row forward (the skip branch writes the row
        // before the readings get to it, and first-writer-wins means a later position cannot rewrite the
        // path) -- so the walk stops there instead of scanning the rest of a 200-syllable name to rediscover
        // a spelling it already has. That also leaves every position past the match at the caller's own
        // reading, which is what this method's contract says and what lets several term sets of one query
        // keep their witnesses instead of each later set zeroing the earlier ones out behind it.
        var walked = positions;
        for (var p = 0; p < positions; p++)
        {
            Array.Clear(next, 0, stride);
            var count = grid.ReadingCount(p);
            for (var j = 0; j <= length; j++)
            {
                if (!reachable[j])
                    continue;

                // Skipping this character's reading: an abbreviation names the characters it wants and
                // lets the rest of the name fall where it may.
                if (!next[j])
                {
                    next[j] = true;
                    prevJ[(p + 1) * stride + j] = j;
                    choiceReading[(p + 1) * stride + j] = 0;
                }

                for (var k = 0; k < count; k++)
                {
                    var reading = grid.Reading(p, k);
                    if (reading.IsEmpty)
                        continue;

                    var max = Math.Min(reading.Length, length - j);
                    var matched = 0;
                    while (matched < max && term[j + matched] == reading[matched])
                        matched++;

                    for (var piece = 1; piece <= matched; piece++)
                    {
                        int target;
                        if (piece == reading.Length)
                            target = j + piece;              // the character spelled out in full
                        else if (piece == 1)
                            target = j + 1;                  // the character abbreviated to its initial
                        else if (j + piece == length)
                            target = length;                 // half-typed, and the term ends there
                        else
                            continue;

                        if (target > length || next[target])
                            continue;

                        next[target] = true;
                        prevJ[(p + 1) * stride + target] = j;
                        choiceReading[(p + 1) * stride + target] = (byte)k;
                    }
                }
            }

            var swapped = reachable;
            reachable = next;
            next = swapped;

            if (reachable[length])
            {
                walked = p + 1;
                break;
            }
        }

        if (!reachable[length])
            return false;

        var cursor = length;
        for (var p = walked; p >= 1; p--)
        {
            var previous = prevJ[p * stride + cursor];
            choice[p - 1] = choiceReading[p * stride + cursor];
            // A row whose cursor did not move carried the term over this position; one that moved is the
            // position that supplied term[previous..cursor), and is therefore a character to light up.
            if (previous != cursor && paintedCount < painted.Length)
                painted[paintedCount++] = p - 1;
            cursor = previous;
        }
        return true;
    }

    private static ReadOnlySpan<char> StripSeparator(ReadOnlySpan<char> term, char separator)
    {
        if (separator == '\0' || term.IndexOf(separator) < 0)
            return term;

        var buffer = _stripped;
        if (buffer == null || buffer.Length < term.Length)
            _stripped = buffer = new char[Math.Max(term.Length, 64)];

        var written = 0;
        foreach (var c in term)
        {
            if (c != separator)
                buffer[written++] = c;
        }
        return buffer.AsSpan(0, written);
    }

    // Contiguous run over one-character-per-position readings: the run occupies consecutive positions,
    // pinned to the alias end when the operator says so, and starts at a position boundary.
    private static bool MatchExactInitials(AliasGridFormat.Index grid, ReadOnlySpan<char> term, AliasGridAnchor anchor, byte[] choice, Span<int> painted, ref int paintedCount)
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
            for (var t = 0; t < term.Length && paintedCount < painted.Length; t++)
                painted[paintedCount++] = start + t;
            return true;
        }
        return false;
    }

    // Contiguous run over whole readings. A reading is several characters long, so the run may begin and
    // end inside one, and may cross position boundaries -- where the next position's reading has to
    // continue the term from its own first emitted character.
    private static bool MatchExactFull(AliasGridFormat.Index grid, ReadOnlySpan<char> term, char separator, AliasGridAnchor anchor, byte[] choice, Span<int> painted, ref int paintedCount)
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
                    if (WalkExact(grid, p, k, offset, term, separator, anchor, choice, painted, ref paintedCount, ref budget))
                        return true;
                }
            }
        }
        return false;
    }

    // A run opens where a reading opens: at the position's first emitted character (its separator, when it
    // has one) or at the character after that separator. An operator chooses which end of the alias the run
    // is pinned to, never whether it may start inside one of the candidate's sounds -- the middle of a
    // reading is not a place any of them starts.
    private static bool StartAllowed(AliasGridFormat.Index grid, int position, int offset, AliasGridAnchor anchor)
    {
        var readingStart = grid.BoundaryBefore(position) ? 1 : 0;
        if (offset != readingStart && !(offset == 0 && readingStart == 1))
            return false;

        return anchor switch
        {
            AliasGridAnchor.AliasStart or AliasGridAnchor.Whole => position == 0 && offset == 0,
            AliasGridAnchor.Boundary => position == 0 || grid.BoundaryBefore(position),
            _ => true, // AliasEnd: pinned to the alias end, and still opens on a reading
        };
    }

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
        Span<int> painted,
        ref int paintedCount,
        ref int budget)
    {
        var t = 0;
        var p = position;
        var k = reading;
        var o = offset;
        // Each start is tried with a fresh walk, so a dead end's partial list is never read: it is reset
        // here and the caller only looks at it when this returns true.
        paintedCount = 0;
        choice[p] = (byte)k;

        while (true)
        {
            if (--budget <= 0)
                return false;

            var text = grid.Reading(p, k);
            var hasSeparator = grid.BoundaryBefore(p);
            var emitted = text.Length + (hasSeparator ? 1 : 0);
            var tBefore = t;
            while (o < emitted && t < term.Length)
            {
                var c = hasSeparator && o == 0 ? separator : text[o - (hasSeparator ? 1 : 0)];
                if (c != term[t])
                    break;
                t++;
                o++;
            }

            if (t > tBefore && paintedCount < painted.Length)
                painted[paintedCount++] = p;

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

    /// <summary>
    /// Writes the alias spelling <paramref name="choice"/> makes on <paramref name="tier"/> as ASCII bytes
    /// into <paramref name="dest"/> (sized from <see cref="Measure"/>), or returns -1 when something it
    /// emits is not one ASCII code unit -- an untransliterated character of the name, or a separator that
    /// is not ASCII. A pure-ASCII alias is matched by the byte-side engine, which reads the same text at
    /// the same offsets as the char side (see FzfByteMatcher), so -1 is a shape verdict, never a miss.
    /// </summary>
    public static int TryMaterializeBytes(AliasGridFormat.Index grid, AliasGridTier tier, char separator, ReadOnlySpan<byte> choice, Span<byte> dest)
    {
        if (tier == AliasGridTier.Full && separator >= 0x80)
            return -1;

        var written = 0;
        for (var p = 0; p < grid.PositionCount; p++)
        {
            if (tier == AliasGridTier.Full && grid.BoundaryBefore(p))
                dest[written++] = (byte)separator;

            var text = grid.Reading(p, choice[p]);
            if (tier == AliasGridTier.Initials)
            {
                // The initials tier emits one unit per position: text[0] is there because every reading is.
                if (text[0] >= 0x80)
                    return -1;
                dest[written++] = (byte)text[0];
                continue;
            }

            foreach (var c in text)
            {
                if (c >= 0x80)
                    return -1;
                dest[written++] = (byte)c;
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
