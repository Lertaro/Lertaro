using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.PinyinAlias;

// Bit-parallel NFA over one folded query term, ported from WinShun's ws::pinyin::Matcher
// (src/core/Pinyin.cpp). The term (2..63 ASCII letters/digits, at least one letter) fits one 64-bit
// state set, where bit p means "the first p letters of the term have been consumed".
//
// Scanning a text, every character must consume at least one letter or the walk dies: an ASCII
// character matches itself (case folded); a Han character may consume any NON-EMPTY PREFIX of any of
// its readings -- its initial, the whole syllable, or a half-typed run, anywhere in the term -- by
// shifting the state set once per syllable letter and accumulating at each step. The matched span is
// therefore contiguous, and every character inside it was consumed: the positions to paint are
// exactly the span. 'v' (ü) also accepts 'u'.
//
// Immutable after construction; one instance serves one term across every candidate and thread.
internal sealed class PinyinTermMatcher : IAliasTermMatcher
{
    private const int MaxTerm = 63; // positions 0..63 fit in one 64-bit state set
    private const int MaxText = 512; // code points, as the reference caps it (file names stay under it)

    private readonly bool _commonOnly;
    private readonly int _length; // 0: not a pinyin term (the provider returns null instead)
    private readonly char _first;
    private readonly ulong[] _at = new ulong[128]; // per ASCII character: term positions holding it

    public PinyinTermMatcher(string term, bool commonOnly)
    {
        _commonOnly = commonOnly;
        if (!IsEligible(term))
            return;

        _length = term.Length;
        _first = term[0];
        for (var p = 0; p < term.Length; p++)
            _at[term[p]] |= 1UL << p;
    }

    public bool IsValid => _length != 0;

    /// <summary>
    /// Whether the (already folded) term is a word this matcher can decide: 2..63 ASCII letters or
    /// digits, at least one of them a letter -- single letters are handled by literal matching only,
    /// exactly as the reference does.
    /// </summary>
    public static bool IsEligible(string term)
    {
        if (term.Length is < 2 or > MaxTerm)
            return false;

        var letter = false;
        foreach (var c in term)
        {
            if (c is >= 'a' and <= 'z')
                letter = true;
            else if (c is < '0' or > '9')
                return false;
        }
        return letter;
    }

    public bool TryFind(ReadOnlySpan<char> text, Span<bool> painted, out int start, out int end)
    {
        start = -1;
        end = -1;
        if (_length == 0 || text.Length == 0)
            return false;

        var done = 1UL << _length;
        var points = 0; // code points seen so far; the reference truncates the text at 512
        var i = 0; // UTF-16 unit index
        while (i < text.Length && points < MaxText)
        {
            var begin = i;
            var first = Decode(text, ref i);
            points++;
            if (!CanStart(first))
                continue;

            var states = 1UL;
            var j = begin;
            while (j < text.Length)
            {
                var (whole, prefix) = Step(Decode(text, ref j), states);
                // A term may complete on a half-typed syllable -- but only HERE, on the character that
                // finishes it. Earlier characters contribute from `whole`, so their syllable must have been
                // consumed completely or as its initial: "lany" cannot take "an" out of tian's middle.
                if ((prefix & done) != 0)
                {
                    start = begin;
                    end = j;
                    for (var k = begin; k < end && k < painted.Length; k++)
                        painted[k] = true;
                    return true;
                }
                states = whole;
                if (states == 0)
                    break;
            }
        }
        return false;
    }

    // From the term positions reached so far, those reached after consuming `cp`.
    private (ulong Whole, ulong AnyPrefix) Step(int cp, ulong states)
    {
        if (cp < 0x80)
        {
            var ascii = (states & _at[Fold((char)cp)]) << 1;
            return (ascii, ascii);
        }

        // Anything this table does not read has nothing to consume: the walk dies here, exactly as
        // the reference dies on a character outside its Han range.
        if (cp > 0xFFFF || !PinyinEngine.TryGetPinyins((char)cp, out var readings))
            return (0, 0);

        var count = _commonOnly && readings.Length > 1 ? 1 : readings.Length;
        ulong whole = 0;
        ulong prefix = 0;
        for (var k = 0; k < count; k++)
        {
            // Walk the syllable's letters. `t` after every letter is a prefix of it; only the state after the
            // FINAL letter is the syllable consumed whole. An initial is the first letter, which is a prefix and
            // therefore also counts as "whole" for a character that is not the one finishing the term.
            var t = states;
            var first = true;
            foreach (var letter in readings[k])
            {
                var l = (byte)letter;
                t = (t & (letter == 'v' ? _at[l] | _at['u'] : _at[l])) << 1;
                if (t == 0)
                    break;
                prefix |= t;
                if (first)
                {
                    whole |= t; // the initial (one letter) is a complete unit of its own
                    first = false;
                }
            }

            if (t != 0)
                whole |= t; // the syllable consumed to its end
        }
        return (whole, prefix);
    }

    // Whether a match may open on this character: an ASCII character must equal the term's first
    // character (folded), a Han character must have a reading whose first letter does.
    private bool CanStart(int cp)
    {
        if (cp < 0x80)
            return Fold((char)cp) == _first;

        if (cp > 0xFFFF || !PinyinEngine.TryGetPinyins((char)cp, out var readings))
            return false;
        foreach (var reading in readings)
            if (reading.Length > 0 && reading[0] == _first)
                return true;
        return false;
    }

    // One code point at text[i], advancing i past it (surrogate pair = two units; a lone half is
    // returned as itself and simply has no readings).
    private static int Decode(ReadOnlySpan<char> text, ref int i)
    {
        var c = text[i++];
        if (char.IsHighSurrogate(c) && i < text.Length && char.IsLowSurrogate(text[i]))
            return char.ConvertToUtf32(c, text[i++]);
        return c;
    }

    private static char Fold(char c) => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
}
