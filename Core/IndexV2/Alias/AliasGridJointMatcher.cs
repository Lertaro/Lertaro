namespace Lertaro.Core.IndexV2.Alias;

using Lertaro.Core.SearchIndex.Fzf;

/// <summary>
/// Decides EVERY term set of one query against a reading grid in a single walk, so the reading choice they
/// share is chosen once for all of them.
/// </summary>
/// <remarks>
/// <see cref="AliasGridMatcher"/>'s walks each answer "can THIS term be spelled", and the caller merges the
/// answers into one reading-per-position array -- which a later set may overwrite. Two sets that both reach
/// for the same polyphonic character can need DIFFERENT readings of it (屏 = bing / ping: one term wants its
/// 'p', another its 'b'), and whichever set is merged last decides what the materialized alias says. Since
/// that alias is what the matcher then scores, a query's answer depended on the order its space-separated
/// terms were typed -- while <see cref="FzfPattern"/> matches term sets independently and never asks for any
/// order. This walk answers what the merge was approximating: does there EXIST one reading choice that
/// satisfies every set at once.
///
/// State = (position, one progress counter per set). A state being reachable after the first p positions
/// means "some choice of readings over those positions leaves every set's term this far spelled", and a
/// position extends it by the pieces its ONE chosen reading can form for every set simultaneously -- a set
/// may also take nothing from it. Sets may overlap: one reading can give up its first letter to one term and
/// its whole body to another, exactly as one spelling served both when the query was matched against a
/// string alias.
///
/// The per-piece rule is the single-term walk's (see AliasGridMatcher.MatchSyllableFull): a piece starts at
/// its reading's first character and is the whole reading, its initial, or -- only where that finishes the
/// term -- half of it. Only the sharing of the reading per position is new.
///
/// It declines, without rewriting anything, the shapes it does not own: a query with a single set (the
/// per-term walk answers that one exactly), a precise operator (which keeps its own anchored contiguous
/// walk), a set whose alternatives are not the same letters (a provider's non-pinyin query form), more sets
/// than its budget, and a walk past its cell or transition ceiling. The caller's per-set merge answers all
/// of those, so nothing that used to be decided stops being decided.
/// </remarks>
internal static class AliasGridJointMatcher
{
    // One position either takes a reading or carries every set past itself; Carried is the latter, and it is
    // what leaves a position no set consumed at the caller's own reading.
    private const byte Carried = byte.MaxValue;

    // Same ceiling as the single-term DP's table and the same reason: the backpointers are one int and one
    // byte per (row, state), and a query shape past that is better answered by the merge.
    private const int MaxJointCells = 1 << 18;

    // The state space is capped on its own too: every position clears the frontier, so a wide product costs
    // on the long names as well, not just on the query.
    private const int MaxJointStates = 1 << 12;

    // A name whose readings serve many sets at once could branch every state at every position. Bailing out
    // hands the query to the merge, which is what answered it until now.
    private const int MaxJointTransitions = 1 << 16;

    private const int MaxJointSets = 8;

    // What one set may take from one reading: carry, initial, whole, term-ending piece.
    private const int OptionsPerDim = 4;

    private const int MaxJointLetters = 1024;

    /// <summary>
    /// The per-query scratch. A class rather than loose [ThreadStatic] arrays because a field initializer on
    /// a thread-static field runs for one thread only -- every other thread would see null.
    /// </summary>
    private sealed class Scratch
    {
        public readonly int[] Stride = new int[MaxJointSets];
        public readonly int[] Radix = new int[MaxJointSets];
        public readonly int[] Length = new int[MaxJointSets];
        public readonly int[] Start = new int[MaxJointSets];
        public readonly int[] Opts = new int[MaxJointSets * OptionsPerDim];
        public readonly int[] OptCount = new int[MaxJointSets];
        public readonly int[] Pick = new int[MaxJointSets];
        public char[] Letters = new char[MaxJointLetters];

        // Scratch's own flag rather than a parameter threaded through the walk: the reconstruction sets it
        // where it already counts the pieces. Reset at the top of every call.
        public bool SpelledOut;
    }

    [ThreadStatic] private static Scratch? _scratch;
    [ThreadStatic] private static int[]? _prev;
    [ThreadStatic] private static byte[]? _chosen;
    [ThreadStatic] private static bool[]? _now;
    [ThreadStatic] private static bool[]? _next;
    [ThreadStatic] private static int[]? _nowList;
    [ThreadStatic] private static int[]? _nextList;

    /// <summary>
    /// Whether one reading choice of <paramref name="grid"/> spells a term of every set in
    /// <paramref name="sets"/> at once. On true, <paramref name="choice"/> (one byte per position, the
    /// caller's readings wherever no set took a piece) is that choice; on false it is untouched.
    /// </summary>
    /// <param name="spellsAReading">
    /// On true, whether some position gave up more than one letter: the query spelled a reading out instead
    /// of abbreviating every character of it. Same meaning as in SearchMatcherGridAliases.SetsWitness, which
    /// uses it to choose the alias quality bar.
    /// </param>
    public static bool TryMatchSets(
        AliasGridFormat.Index grid,
        FzfTermSet[] sets,
        char separator,
        AliasGridTier tier,
        byte[] choice,
        out bool spellsAReading)
    {
        spellsAReading = false;
        var positions = grid.PositionCount;
        var dims = sets.Length;
        if (dims is < 2 or > MaxJointSets || positions == 0 || choice.Length < positions)
            return false;

        var scratch = _scratch ??= new Scratch();
        scratch.SpelledOut = false;
        if (!CollectTerms(grid, sets, separator, tier, scratch, dims))
            return false;

        // Mixed radix: dim d counts its term's letters in [0, length_d], so the state space is the product of
        // the lengths and the all-satisfied state is its top value.
        long states = 1;
        for (var d = 0; d < dims; d++)
        {
            scratch.Stride[d] = (int)states;
            scratch.Radix[d] = scratch.Length[d] + 1;
            states *= scratch.Radix[d];
            if (states > MaxJointStates || (positions + 1L) * states > MaxJointCells)
                return false;
        }

        var stateCount = (int)states;
        var finalState = stateCount - 1;
        Ensure(stateCount, (positions + 1) * stateCount);
        var prev = _prev!;
        var chosen = _chosen!;
        var now = _now!;
        var next = _next!;
        var nowList = _nowList!;
        var nextList = _nextList!;

        Array.Clear(now, 0, stateCount);
        now[0] = true;
        nowList[0] = 0;
        var nowCount = 1;
        var transitions = 0;
        var walked = 0;

        for (var p = 0; p < positions; p++)
        {
            Array.Clear(next, 0, stateCount);
            var nextCount = 0;
            var row = (p + 1) * stateCount;

            for (var a = 0; a < nowCount; a++)
            {
                var state = nowList[a];

                // Nobody takes a piece from this position: it keeps whatever reading the caller put there.
                if (!next[state])
                {
                    next[state] = true;
                    nextList[nextCount++] = state;
                    prev[row + state] = state;
                    chosen[row + state] = Carried;
                }

                var count = grid.ReadingCount(p);
                for (var k = 0; k < count; k++)
                {
                    var reading = grid.Reading(p, k);
                    // The initials tier emits one character per position, whatever the reading's length.
                    var emit = tier == AliasGridTier.Initials ? 1 : reading.Length;
                    if (emit == 0 || !DimOptions(scratch, reading[..emit], state, dims))
                        continue;

                    // Every combination of the per-dim choices is a successor: the SAME reading serves them
                    // all, which is the whole point of walking the sets together.
                    var lastDim = false;
                    while (!lastDim)
                    {
                        var delta = 0;
                        var any = false;
                        for (var d = 0; d < dims; d++)
                        {
                            var step = scratch.Opts[(d * OptionsPerDim) + scratch.Pick[d]];
                            if (step == 0)
                                continue;
                            any = true;
                            delta += step * scratch.Stride[d];
                        }

                        if (any)
                        {
                            var successor = state + delta;
                            if (!next[successor])
                            {
                                next[successor] = true;
                                nextList[nextCount++] = successor;
                                prev[row + successor] = state;
                                chosen[row + successor] = (byte)k;
                                if (++transitions > MaxJointTransitions)
                                    return false;
                            }
                        }

                        var carry = 0;
                        while (carry < dims)
                        {
                            if (++scratch.Pick[carry] < scratch.OptCount[carry])
                                break;
                            scratch.Pick[carry] = 0;
                            carry++;
                        }
                        lastDim = carry == dims;
                    }
                }
            }

            if (next[finalState])
            {
                walked = p + 1;
                break;
            }

            (_now, _next) = (next, now);
            (_nowList, _nextList) = (nextList, nowList);
            now = _now;
            next = _next;
            nowList = _nowList;
            nextList = _nextList;
            nowCount = nextCount;
        }

        if (walked == 0)
            return false;

        Reconstruct(choice, scratch, prev, chosen, dims, walked, stateCount, finalState);
        spellsAReading = scratch.SpelledOut;
        return true;
    }

    /// <summary>
    /// Packs one letter string per set, stripped of the provider's separator, and refuses the shapes this
    /// walk does not own: an operator term, a set whose alternatives are not the same letters, a set with
    /// nothing positive to satisfy, and a term no reading of this name can even start.
    /// </summary>
    private static bool CollectTerms(AliasGridFormat.Index grid, FzfTermSet[] sets, char separator, AliasGridTier tier, Scratch scratch, int dims)
    {
        var totalLetters = 0;
        for (var s = 0; s < dims; s++)
        {
            var length = -1;
            foreach (var term in sets[s].Terms)
            {
                if (term.Inverse)
                    continue;
                if (term.Kind is not (FzfTermKind.Fuzzy or FzfTermKind.Exact))
                    return false;

                var letters = AliasGridMatcher.StripSeparator(term.Text, separator);
                if (letters.IsEmpty)
                    return false;
                if (length >= 0)
                {
                    if (letters.Length != length)
                        return false;
                    for (var i = 0; i < length; i++)
                        if (scratch.Letters[scratch.Start[s] + i] != letters[i])
                            return false;
                    continue;
                }

                if (totalLetters + letters.Length > scratch.Letters.Length)
                    return false;

                // The same cheap refusal the single-term walks use first: every piece starts where a reading
                // starts, so a letter no reading of this name begins with ends the query here.
                if (tier == AliasGridTier.Initials
                    ? !grid.StartsFromAll(letters)
                    : !grid.StartsFrom(letters[0]))
                    return false;

                letters.CopyTo(scratch.Letters.AsSpan(totalLetters));
                scratch.Start[s] = totalLetters;
                scratch.Length[s] = letters.Length;
                totalLetters += letters.Length;
                length = letters.Length;
            }

            if (length < 0)
                return false;
        }

        return true;
    }

    // What each set may take from this position's reading, given how far it is: carry (0), the initial (1),
    // the whole reading, or -- only where that finishes the term -- the half-typed tail. False when no set
    // can take anything, which is the "carry" option the caller already recorded.
    private static bool DimOptions(Scratch scratch, ReadOnlySpan<char> emitted, int state, int dims)
    {
        var any = false;
        for (var d = 0; d < dims; d++)
        {
            var slot = d * OptionsPerDim;
            var count = 1;
            scratch.Opts[slot] = 0;

            var progress = (state / scratch.Stride[d]) % scratch.Radix[d];
            var left = scratch.Length[d] - progress;
            if (left > 0)
            {
                var letters = scratch.Letters.AsSpan(scratch.Start[d] + progress, left);
                var matched = 0;
                var cap = Math.Min(emitted.Length, letters.Length);
                while (matched < cap && letters[matched] == emitted[matched])
                    matched++;

                if (matched >= 1)
                {
                    scratch.Opts[slot + count++] = 1;
                    if (matched == emitted.Length && emitted.Length > 1)
                        scratch.Opts[slot + count++] = emitted.Length;
                    if (left >= 2 && left <= matched && left != emitted.Length)
                        scratch.Opts[slot + count++] = left;
                }
            }

            scratch.OptCount[d] = count;
            scratch.Pick[d] = 0;
            if (count > 1)
                any = true;
        }

        return any;
    }

    // Walks the recorded path back from the all-satisfied state: a row marked Carried left the caller's
    // reading alone, one that took a reading is the position that served the pieces -- and a piece of two or
    // more letters means the query spelled a reading out rather than abbreviating it.
    private static void Reconstruct(
        byte[] choice,
        Scratch scratch,
        int[] prev,
        byte[] chosen,
        int dims,
        int walked,
        int stateCount,
        int finalState)
    {
        var state = finalState;
        for (var row = walked; row >= 1; row--)
        {
            var previous = prev[(row * stateCount) + state];
            var k = chosen[(row * stateCount) + state];
            if (k != Carried)
            {
                choice[row - 1] = k;
                for (var d = 0; d < dims; d++)
                {
                    var before = (previous / scratch.Stride[d]) % scratch.Radix[d];
                    var after = (state / scratch.Stride[d]) % scratch.Radix[d];
                    if (after - before >= 2)
                    {
                        scratch.SpelledOut = true;
                        break;
                    }
                }
            }

            state = previous;
        }
    }

    private static void Ensure(int states, int cells)
    {
        if (_prev == null || _prev.Length < cells)
        {
            _prev = new int[cells];
            _chosen = new byte[cells];
        }

        if (_now == null || _now.Length < states)
        {
            _now = new bool[states];
            _next = new bool[states];
            _nowList = new int[states];
            _nextList = new int[states];
        }
    }
}
