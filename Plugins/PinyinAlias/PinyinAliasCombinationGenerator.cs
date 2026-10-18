namespace Lertaro.Plugins.PinyinAlias;

// The reading-spelling helpers behind PinyinAliasProvider: the per-position syllable lists (which the
// host reads for its bake-time prefilter mask and for MapAliasToSourceIndices) and the single-reading
// spellings the mixed-alphabet lane matches as text. Extracted into its own class purely to keep
// PinyinAliasProvider.cs under the repo's per-file line limit; GetSyllableLists therefore stays
// internal-visible here rather than private.
internal static class PinyinAliasCombinationGenerator
{
    private static readonly string[][] AsciiSyllableCache;

    // Generation scratch, reused per thread: only the returned alias strings themselves are allocated
    // per call. The full-spelling buffer grows with the name because a spelling must carry every
    // syllable of it; a name is bounded by the file-system limit, not by a budget of this class's.
    [ThreadStatic] private static string[][]? _syllableScratch;
    [ThreadStatic] private static char[]? _fullBufferScratch;
    [ThreadStatic] private static char[]? _initialsScratch;
    [ThreadStatic] private static List<string>? _resultListScratch;

    // '|' is the one ASCII character that can't pass through literally: every alias consumer (the '|' a
    // host reads as the separator between alternative polyphonic readings, in HighlightMask.MarkViaAliasProviders,
    // FzfPattern.TryMatch/IsAcceptableAliasMatch's own alias-side splitting and Snapshot's alias blobs)
    // treats a '|' found INSIDE a generated alias string as that separator. A source text that happens to
    // contain a literal '|' (e.g. a browser tab title like "example.com | 代理", which uses it as a plain
    // visual separator) would otherwise pass that character straight through into the alias, getting it
    // misread as a reading boundary -- splitting one continuous alias into two fragments that individually
    // either can't find the query or can't map back to the right source positions, silently losing
    // highlighting (and, in the worst case, a match) for perfectly good source text.
    // Substituting a control character that can never occur in real generated aliases sidesteps the
    // ambiguity entirely: it can never coincide with the genuine outer-join '|', so every consumer's
    // existing split-on-'|' logic keeps working exactly as intended for real alternative readings.
    private const char PipePlaceholder = (char)1; // U+0001 (SOH) -- a control character that can never occur in real generated aliases

    static PinyinAliasCombinationGenerator()
    {
        AsciiSyllableCache = new string[128][];
        for (var i = 0; i < 128; i++)
        {
            var c = (char)i;
            AsciiSyllableCache[i] = new string[] { c == '|' ? PipePlaceholder.ToString() : c.ToString().ToLowerInvariant() };
        }
    }

    // The one-reading expansion: initials plus the full spelling built from every character's first
    // candidate syllable, appended to the caller's list.
    private static void GenerateSingleReading(string text, string[][] lists, List<string> result)
    {
        var initialsArr = _initialsScratch;
        if (initialsArr == null || initialsArr.Length < text.Length)
            _initialsScratch = initialsArr = new char[Math.Max(text.Length, 64)];

        var fullLen = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var s = lists[i][0];
            initialsArr[i] = s.Length > 0 ? s[0] : '\0';
            if (PinyinAliasFormat.NeedsSeparatorBefore(text, i))
                fullLen++;
            fullLen += s.Length;
        }

        var initialAlias = new string(initialsArr, 0, text.Length);
        result.Add(initialAlias);

        var fullBuffer = _fullBufferScratch;
        if (fullBuffer == null || fullBuffer.Length < fullLen)
            _fullBufferScratch = fullBuffer = new char[Math.Max(fullLen, 256)];

        var offset = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var s = lists[i][0];
            if (PinyinAliasFormat.NeedsSeparatorBefore(text, i))
                fullBuffer[offset++] = PinyinAliasFormat.SyllableSeparator;
            s.CopyTo(0, fullBuffer, offset, s.Length);
            offset += s.Length;
        }
        var fullAlias = new string(fullBuffer, 0, fullLen);
        if (fullAlias != initialAlias)
            result.Add(fullAlias);
    }

    public static string[][] GetSyllableLists(string text)
    {
        var lists = _syllableScratch;
        if (lists == null || lists.Length < text.Length)
            _syllableScratch = lists = new string[Math.Max(text.Length, 64)][];

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (PinyinEngine.TryGetPinyins(c, out var pinyins))
            {
                lists[i] = pinyins;
            }
            else if (c < 128)
            {
                lists[i] = AsciiSyllableCache[c];
            }
            else
            {
                lists[i] = new string[] { char.ToLowerInvariant(c).ToString() };
            }
        }
        return lists;
    }

    /// <summary>
    /// Appends the single-reading spellings of <paramref name="text"/> -- the initials spelling, then
    /// the full spelling when it differs. This is the materialization the mixed-alphabet lane consumes
    /// (IAliasLiveMatcherProvider.AppendSpellings): the same shape the index baked for that lane before
    /// matching went live.
    /// </summary>
    public static void AppendDefaultSpellings(string text, List<string> dest)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var result = _resultListScratch ??= new List<string>(4);
        result.Clear();
        GenerateSingleReading(text, GetSyllableLists(text), result);
        dest.AddRange(result);
        result.Clear();
    }
}
