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
                var cp = Decode(text, ref j);
                states = Step(cp, states);
                if ((states & done) != 0)
                {
                    start = begin;
                    end = j;
                    for (var k = begin; k < end && k < painted.Length; k++)
                        painted[k] = true;
                    return true;
                }
                if (states == 0)
                    break;
            }
        }
        return false;
    }

    // From the term positions reached so far, those reached after consuming `cp`.
    private ulong Step(int cp, ulong states)
    {
        if (cp < 0x80)
            return (states & _at[Fold((char)cp)]) << 1;

        // Anything this table does not read has nothing to consume: the walk dies here, exactly as
        // the reference dies on a character outside its Han range.
        if (cp > 0xFFFF || !PinyinEngine.TryGetPinyins((char)cp, out var readings))
            return 0;

        // Common-only treats the table's two most frequent readings as common. The table is ordered by
        // frequency, so this covers zhong/chong, chang/zhang, xing/hang and le/yue without a word
        // dictionary; rarer readings still belong to the all-readings walk.
        var count = _commonOnly ? Math.Min(readings.Length, 2) : readings.Length;
        ulong next = 0;
        for (var k = 0; k < count; k++)
            next |= Consume(states, readings[k]);
        return next;
    }

    private ulong Consume(ulong states, string reading)
    {
        // Every non-empty prefix of the syllable: after k letters, `t` holds the positions
        // reached. 'v' (ü) also accepts 'u'.
        var t = states;
        ulong next = 0;
        foreach (var letter in reading)
        {
            var l = (byte)letter;
            t = (t & (letter == 'v' ? _at[l] | _at['u'] : _at[l])) << 1;
            if (t == 0)
                break;
            next |= t;
        }
        return next;
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
