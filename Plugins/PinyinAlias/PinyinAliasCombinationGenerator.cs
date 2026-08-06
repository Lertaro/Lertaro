namespace Lertaro.Plugins.PinyinAlias;

// The string-path combination-generation core behind PinyinAliasProvider.GetAliases -- extracted into
// its own class (composition, not a partial class) purely to keep PinyinAliasProvider.cs under the
// repo's per-file line limit. GetSyllableLists is also called directly by
// PinyinAliasProvider.MapAliasToSourceIndices, so it stays internal-visible here rather than private.
internal static class PinyinAliasCombinationGenerator
{
    private static readonly string[][] AsciiSyllableCache;

    // Generation scratch, reused per thread: only the returned alias strings themselves are
    // allocated per call. _comboFullScratch is deliberately FIXED at 256 chars -- the combination
    // path's max-full-pinyin cap is part of the output contract (longer branches are pruned), and a
    // growable buffer here would make results depend on what a previous call happened to grow it to.
    [ThreadStatic] private static string[][]? _syllableScratch;
    [ThreadStatic] private static char[]? _fullBufferScratch;
    [ThreadStatic] private static char[]? _comboFullScratch;
    [ThreadStatic] private static char[]? _initialsScratch;
    [ThreadStatic] private static List<string>? _fullsListScratch;
    [ThreadStatic] private static List<string>? _initialsListScratch;
    [ThreadStatic] private static List<string>? _resultListScratch;

    // '|' is the one ASCII character that can't pass through literally: every alias consumer (this
    // class's own JoinUnique, HighlightMask.MarkViaAliasProviders, and FzfPattern.TryMatch/
    // IsAcceptableAliasMatch's own alias-side splitting) treats a '|' found INSIDE a generated alias
    // string as the separator between alternative polyphonic readings. A source text that happens to
    // contain a literal '|' (e.g. a browser tab title like "example.com | 代理", which uses it as a
    // plain visual separator) would otherwise pass that character straight through into the alias,
    // getting it misread as a reading boundary -- splitting one continuous alias into two fragments
    // that individually either can't find the query or can't map back to the right source positions,
    // silently losing highlighting (and, in the worst case, a match) for perfectly good source text.
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

    public static string[] GenerateAliases(string text)
    {
        if (text.Length == 1)
        {
            // Single character fallback (needed for single-character queries)
            return PinyinEngine.TryGetPinyins(text[0], out var pinyins)
                ? pinyins
                : Array.Empty<string>();
        }

        var result = _resultListScratch ??= new List<string>(4);
        result.Clear();

        var lists = GetSyllableLists(text);

        var totalCombinations = 1;
        for (var i = 0; i < text.Length; i++)
        {
            totalCombinations *= lists[i].Length;
            if (totalCombinations > 32)
                break;
        }

        if (totalCombinations == 1)
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
            return result.ToArray();
        }

        var fullPinyins = _fullsListScratch ??= new List<string>(32);
        var initials = _initialsListScratch ??= new List<string>(32);
        fullPinyins.Clear();
        initials.Clear();
        var count = 0;
        var steps = 0;

        var fullBufferTemp = _comboFullScratch ??= new char[256];
        var initialsBuffer = _initialsScratch;
        if (initialsBuffer == null || initialsBuffer.Length < text.Length)
            _initialsScratch = initialsBuffer = new char[Math.Max(text.Length, 64)];

        // Generate combinations. Since we concatenate them, we can safely allow up to 32 combinations
        // to support longer polyphonic names without database explosion.
        GenerateCombinations(text, lists, text.Length, 0, 0, fullPinyins, initials, fullBufferTemp, initialsBuffer, ref count, ref steps);

        var joinedInitials = JoinUnique(initials);
        if (joinedInitials != null)
            result.Add(joinedInitials);

        var joinedFulls = JoinUnique(fullPinyins);
        if (joinedFulls != null && !joinedFulls.Equals(joinedInitials, StringComparison.OrdinalIgnoreCase))
            result.Add(joinedFulls);

        return result.ToArray();
    }

    // Dedup preserving insertion order (List.Contains semantics), '|'-joined; n <= 32 so a linear
    // scan beats a HashSet allocation at this size.
    private static string? JoinUnique(List<string> values)
    {
        string? single = null;
        List<string>? unique = null;
        foreach (var v in values)
        {
            if (string.IsNullOrWhiteSpace(v))
                continue;
            if (single == null)
            {
                single = v;
                continue;
            }
            if (unique == null)
            {
                if (v == single)
                    continue;
                unique = new List<string>(4) { single, v };
                continue;
            }
            if (!unique.Contains(v))
                unique.Add(v);
        }

        if (unique != null)
            return string.Join('|', unique);
        return single;
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

    private static void GenerateCombinations(
        string text,
        string[][] lists,
        int listCount,
        int index,
        int currentFullLength,
        List<string> fullPinyins,
        List<string> initials,
        char[] fullBuffer,
        char[] initialsBuffer,
        ref int count,
        ref int steps)
    {
        // Steps budget: the 32-combination cap below only counts FULL-depth completions, but the
        // fullBuffer-overflow check prunes branches BEFORE full depth -- a long name (full pinyin
        // longer than the buffer) dense with polyphonic characters means no branch ever completes,
        // the cap never fires, and the recursion explores the whole combinatorial tree (a 240-char
        // all-polyphonic name explored ~2^55 paths and hung the process). The budget covers every
        // legitimate enumeration and turns the pathological case into an immediate bounded bail-out.
        if (++steps > listCount * 32 + 256) return;
        if (count >= 32) return; // Limit to 32 combinations to prevent combinatorial explosion

        if (index == listCount)
        {
            fullPinyins.Add(new string(fullBuffer, 0, currentFullLength));
            initials.Add(new string(initialsBuffer, 0, listCount));
            count++;
            return;
        }

        var elements = lists[index];
        var separate = PinyinAliasFormat.NeedsSeparatorBefore(text, index);
        foreach (var element in elements)
        {
            var at = currentFullLength;
            if (separate)
            {
                if (at + 1 > fullBuffer.Length)
                    continue;
                fullBuffer[at++] = PinyinAliasFormat.SyllableSeparator;
            }

            if (at + element.Length <= fullBuffer.Length)
            {
                element.CopyTo(0, fullBuffer, at, element.Length);
                initialsBuffer[index] = element.Length > 0 ? element[0] : '\0';
                GenerateCombinations(text, lists, listCount, index + 1, at + element.Length, fullPinyins, initials, fullBuffer, initialsBuffer, ref count, ref steps);
            }
        }
    }

}
