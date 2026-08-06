namespace Lertaro.Plugins.PinyinAlias;

// Rewrites a typed query into the same syllable-delimited shape the generated aliases use, so that
// "zhengshu" can still reach 证书 once the alias itself is "…zheng<SEP>shu" rather than one run of
// letters. This is the query half of the boundary contract: the alias side inserts the delimiter, and
// this side works out where the same delimiter would fall in what the user typed.
//
// It also carries the actual fix. A query only expands if every piece of it is a real syllable, so
// "gsh" -- three letters that are not a syllable sequence -- produces nothing here and is left with
// only the initials alias to match against, where it correctly finds no contiguous "gsh". Before the
// delimiter existed, that same query matched the concatenated full pinyin of 管理用户证书 across the
// 证/书 boundary ("…zhen[gsh]u") and dragged in a screenful of unrelated System Settings entries.
internal static class PinyinQuerySegmenter
{
    // Enough for any real query; a pathological string that segments many ways stops here rather than
    // multiplying how many alternatives each candidate is matched against. Mirrors the 32-combination
    // cap the alias generator applies on its own side.
    private const int MaxForms = 8;
    private const int MaxQueryLength = 32;
    // Readings to enumerate before ranking. Has to exceed MaxForms by a wide margin: the fewest-piece
    // reading is often reached late, after the greedy longest-first walk has produced many worse ones.
    private const int MaxCandidates = 512;
    private const int StepBudget = 20_000;

    // Declared before the sets that read it: static field initializers run in declaration order, so a
    // later position would leave BuildSyllables reading a null array.
    private static readonly string[] InterjectionOnly = { "m", "n", "ng", "hm", "hng" };

    private static readonly HashSet<string> Syllables = BuildSyllables();
    private static readonly HashSet<string> Prefixes = BuildPrefixes();

    // Span lookups: the walk tests up to MaxSyllableLength slices at every position, and materializing
    // each one as a string allocated ~200 throwaway strings per query for nothing.
    private static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> SyllableSpans =
        Syllables.GetAlternateLookup<ReadOnlySpan<char>>();
    private static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> PrefixSpans =
        Prefixes.GetAlternateLookup<ReadOnlySpan<char>>();

    // Readings that only ever belong to interjections (嗯 n/ng, 呒 m, 噷 hm, 哼 hng). They are real
    // table syllables, so alias generation must keep emitting them, but as a piece of a TYPED query
    // they are never what anyone meant -- they only ever appeared by soaking up the tail of the
    // syllable before them ("zheng" read as "zhe"+"ng", "bang"+"e"+"ng" instead of "ban"+"geng"),
    // burning slots in the per-query form budget on readings no candidate will ever have.
    //
    // Only the whole-syllable set drops them; they stay available as PREFIXES, since a half-typed
    // trailing syllable legitimately passes through them ("ni"+"h" on the way to 你好).
    //
    // Vowel-initial syllables (a/e/o/ai/an/ang/ao/ei/en/eng/er/ou) are deliberately NOT here. They look
    // similar -- they are also what lets a preceding syllable steal a consonant -- but they are real
    // and common (阿里 a+li, 西安 xi+an, 恩施 en+shi, 儿童 er+tong), and that ambiguity is inherent to
    // pinyin itself, which is why written pinyin needs an apostrophe for Xi'an. Both readings are kept
    // and the candidate decides.
    private static readonly HashSet<string> Interjections = new(InterjectionOnly, StringComparer.Ordinal);

    private static HashSet<string> BuildSyllables()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in PinyinEngine.AllSyllables)
        {
            if (!string.IsNullOrEmpty(s))
                set.Add(s);
        }
        return set;
    }

    // Every proper prefix of every syllable. The final piece of a query is allowed to be one of these
    // rather than a whole syllable, because in a search-as-you-type box the last syllable is usually
    // still half-typed -- "zhengsh" has to keep reaching 证书 on the way to "zhengshu".
    private static HashSet<string> BuildPrefixes()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in PinyinEngine.AllSyllables)
        {
            for (var len = 1; len < s.Length; len++)
                set.Add(s[..len]);
        }
        return set;
    }

    /// <summary>
    /// The delimiter-joined readings of <paramref name="query"/>, or an empty array when it is not a
    /// syllable sequence at all. A single-syllable query returns nothing: it contains no boundary, so
    /// it already matches the alias as typed and an extra identical form would be pure overhead.
    /// </summary>
    public static string[] Segment(string query)
    {
        if (string.IsNullOrEmpty(query) || query.Length > MaxQueryLength)
            return Array.Empty<string>();

        for (var i = 0; i < query.Length; i++)
        {
            if (query[i] is < 'a' or > 'z')
                return Array.Empty<string>();
        }

        // A query that is itself a syllable (or the start of one) is a user part-way through typing a
        // single syllable, so its pieces all have to be whole syllables: "xian" may still split as
        // "xi"+"an" for 西安, but "zheng" must not become "zhen"+"g", which would reach every 真高-like
        // name whose next syllable merely starts with g. Anything that cannot be one syllable is a
        // genuine multi-syllable query, and there the trailing piece is allowed to be half-typed.
        var allowPartialTail = !Syllables.Contains(query) && !Prefixes.Contains(query);

        // Collect widely, then keep the fewest-piece readings. Taking the first MaxForms the walk
        // happens to reach loses real answers: exploring longest-piece-first sinks the whole budget
        // into the first greedy choice's subtree, so "bangengcuanshun" filled up on variations of
        // "bang/eng/..." (down to junk like "cu/a/n") and never backtracked to the intended
        // "ban/geng/cuan/shun". Piece count is the useful signal -- a reading that needs more pieces to
        // cover the same letters is splitting syllables that did not need splitting.
        var all = new List<string[]>();
        var steps = 0;
        Walk(query, 0, new List<string>(), all, allowPartialTail, ref steps);
        if (all.Count == 0)
            return Array.Empty<string>();

        // Fewest pieces first, then fewest interjection-only pieces. Ranking rather than excluding them
        // is what keeps both properties: a reading that genuinely needs 嗯/呒 is still produced (dropping
        // those syllables outright measurably lost real names), but one that only reached them by
        // soaking up the previous syllable's tail can never crowd a real reading out of the budget.
        all.Sort((a, b) =>
        {
            var byPieces = a.Length.CompareTo(b.Length);
            return byPieces != 0 ? byPieces : InterjectionCount(a).CompareTo(InterjectionCount(b));
        });
        var take = Math.Min(MaxForms, all.Count);
        var forms = new string[take];
        for (var i = 0; i < take; i++)
            forms[i] = string.Join(SyllableSeparatorString, all[i]);
        return forms;
    }

    private static readonly string SyllableSeparatorString = PinyinAliasFormat.SyllableSeparator.ToString();

    private static int InterjectionCount(string[] pieces)
    {
        var n = 0;
        foreach (var p in pieces)
        {
            if (Interjections.Contains(p))
                n++;
        }
        return n;
    }

    // Enumerates readings longest-piece-first. The caller ranks them; this only has to stay bounded,
    // which the step budget does -- a long all-ambiguous query would otherwise explore a combinatorial
    // tree, the same failure the alias generator guards against on its own side.
    private static void Walk(string query, int start, List<string> pieces, List<string[]> found,
        bool allowPartialTail, ref int steps)
    {
        if (++steps > StepBudget || found.Count >= MaxCandidates)
            return;

        if (start == query.Length)
        {
            // One piece means no boundary was found inside the query, so the delimited form would be
            // the query itself -- nothing to add.
            if (pieces.Count > 1)
                found.Add(pieces.ToArray());
            return;
        }

        var maxLen = Math.Min(PinyinAliasFormat.MaxSyllableLength, query.Length - start);
        for (var len = maxLen; len >= 1; len--)
        {
            var span = query.AsSpan(start, len);
            var isLast = start + len == query.Length;
            if (!SyllableSpans.Contains(span) && !(isLast && allowPartialTail && PrefixSpans.Contains(span)))
                continue;

            pieces.Add(query.Substring(start, len));
            Walk(query, start + len, pieces, found, allowPartialTail, ref steps);
            pieces.RemoveAt(pieces.Count - 1);

            if (steps > StepBudget || found.Count >= MaxCandidates)
                return;
        }
    }
}
