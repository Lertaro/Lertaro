namespace Lertaro.Core.SearchIndex;

using Lertaro.Core.SearchIndex.Fzf;

// Two rules an alias match has to satisfy that are about the provider's own writing system rather than
// about text matching, kept together because every alias-matching site applies both in the same breath.
//
// Kept out of FzfPattern so the sites that fall back to a provider's aliases share one definition instead
// of each re-deriving it (and drifting).
internal static class AliasMatchRules
{
    /// <summary>
    /// Whether a match that begins at <paramref name="matchStart"/> inside <paramref name="alias"/> sits
    /// on a syllable boundary.
    /// </summary>
    /// <remarks>
    /// Enforced only for PRECISE aliases (a provider that declares a separator, matched by a term that is
    /// not fuzzy). This is what closes the cross-syllable hole described on
    /// <c>IAliasProvider.SyllableSeparator</c>: with the query "ex" split into "e" + "x", "e" matched the
    /// tail of "xue" and "x" the head of "xi", so 学习 and 人行道 came back for letters that never belong
    /// to the same sound.
    ///
    /// An alias containing no separator at all is treated as flat, where every position is a boundary:
    /// that is the shape of the per-character initials alias (one letter per source character, so "x"
    /// legitimately matches the second character of "ex"), and also the shape a full reading takes when
    /// none of its adjacencies is a syllable boundary. Only the START is constrained, so a match may still
    /// stop part-way through its last syllable -- which is what keeps "zhengsh" reaching 证书 mid-typing.
    ///
    /// ponytail: the caller checks the start of the first occurrence it found. Rejecting a misaligned first
    /// occurrence while a later aligned one exists needs the same fragment to appear both mid-syllable and
    /// at a syllable start in one alias; the flat initials alias still covers any query the user actually
    /// abbreviated to.
    /// </remarks>
    public static bool IsBoundaryAligned(char separator, ReadOnlySpan<char> alias, int matchStart)
    {
        // No declared structure: nothing to align to, so the provider opts out entirely.
        if (separator == '\0' || matchStart <= 0 || matchStart > alias.Length)
            return true;

        // A flat alias (no separator anywhere) has a boundary at every position.
        if (alias.IndexOf(separator) < 0)
            return true;

        // The character before the match must be a syllable boundary. '|' also counts: a polyphonic
        // provider emits several readings as one '|'-joined string, and the matcher scores each segment
        // independently with offsets rebased onto the whole string -- so a match opening a later reading
        // sits right after that '|' and is a boundary in exactly the sense that matters.
        //
        // A character the provider did NOT transliterate counts too, and has to: the delimiter is emitted
        // only BETWEEN two transliterated characters (see PinyinAliasFormat.NeedsSeparatorBefore) precisely
        // because a passed-through character is already a visible boundary -- nothing can hide inside it.
        // So a reading that follows one opens on a real
        // boundary that no separator points at. Missing that rejected the one alias a mixed name had:
        // "、gao<SEP>xiao" in the ticket's 188-character name matched every alias but the one carrying 高效,
        // because '高' happens to follow the name's own "、".
        var previous = alias[matchStart - 1];
        return previous == separator || previous == '|' || !IsAsciiLetter(previous);
    }

    // Only an ASCII letter can be the inside of a reading: every reading a provider emits is letters, and
    // every position that is NOT preceded by one either follows the separator, a '|', a passed-through
    // character, or (in the byte twin) a multi-byte character's continuation byte. A non-ASCII letter is
    // passed-through text as well -- no provider transliterates into it.
    private static bool IsAsciiLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    /// <summary>Byte twin of <see cref="IsBoundaryAligned(char, ReadOnlySpan{char}, int)"/>.</summary>
    /// <remarks>
    /// For the hot path that matches a baked alias straight from its UTF-8 without decoding. Every
    /// separator a provider declares is ASCII (pinyin's is U+0002), so it is a single byte equal to the
    /// char; a non-ASCII alias takes the decoded char path instead.
    /// </remarks>
    public static bool IsBoundaryAligned(char separator, ReadOnlySpan<byte> aliasUtf8, int matchStart)
    {
        if (separator == '\0' || matchStart <= 0 || matchStart > aliasUtf8.Length)
            return true;

        var separatorByte = (byte)separator;
        if (aliasUtf8.IndexOf(separatorByte) < 0)
            return true;

        // '|' as well as the separator, and any byte that cannot be the inside of a reading: see the char
        // overload. A passed-through multi-byte character leaves a continuation byte (>= 0x80) in front,
        // which is exactly as much of a boundary as a passed-through ASCII one.
        var previous = aliasUtf8[matchStart - 1];
        return previous == separatorByte || previous == (byte)'|' || !IsAsciiLetter((char)previous);
    }

    /// <summary>
    /// How wide an occurrence of a <paramref name="termLength"/>-character term may be and still count as
    /// an explanation of a match. Shared by the alias gate and by highlighting, so a candidate cannot be
    /// found by one rule and painted by another: without it, highlighting paints the greedy earliest
    /// subsequence wherever it lands, which on a flattened initials alias is a wide scatter of unrelated
    /// characters (a 7-letter pinyin query lit six characters spread over 116 positions of a 184-character
    /// name).
    /// </summary>
    public static bool IsTightOccurrence(int termLength, int span)
        => span <= Math.Max(termLength * TightPerTermMultiplier, TightPerTermFloor);

    /// <summary>
    /// Positions in <paramref name="text"/> of the narrowest in-order occurrence of
    /// <paramref name="term"/> as a subsequence, or null when the term does not occur at all.
    /// </summary>
    /// <remarks>
    /// Forward scan completes the term, backward scan then pulls the start as late as it can: the classic
    /// minimum-window-of-subsequence walk. Shared because both sides need the SAME occurrence -- matching
    /// has to know how tight a term's best occurrence is, and highlighting has to paint that occurrence
    /// rather than a wider one. Left alone, highlighting takes the greedy earliest subsequence, which on a
    /// flattened initials alias scatters across the whole string (a 7-letter pinyin query lit six
    /// characters spread over 116 positions of a 184-character name) while matching had accepted a tight
    /// occurrence elsewhere.
    ///
    /// Callers pass text and term in the same case unless they pass
    /// <paramref name="caseSensitive"/>; the comparison folds otherwise.
    /// </remarks>
    public static int[]? TightestOccurrence(ReadOnlySpan<char> text, ReadOnlySpan<char> term, bool caseSensitive)
    {
        if (term.IsEmpty || text.Length < term.Length)
            return null;

        int[]? best = null;
        var from = 0;
        while (from < text.Length)
        {
            var ti = 0;
            var j = from;
            while (j < text.Length && ti < term.Length)
            {
                if (CharsEqual(text[j], term[ti], caseSensitive))
                    ti++;
                j++;
            }
            if (ti < term.Length)
                break;

            var end = j - 1;
            ti = term.Length - 1;
            var k = end;
            while (k >= 0 && ti >= 0)
            {
                if (CharsEqual(text[k], term[ti], caseSensitive))
                    ti--;
                k--;
            }

            var windowStart = k + 1;
            if (best == null || end - windowStart < best[^1] - best[0])
            {
                var positions = new int[term.Length];
                var pos = 0;
                ti = 0;
                for (var i = windowStart; i <= end && ti < term.Length; i++)
                {
                    if (CharsEqual(text[i], term[ti], caseSensitive))
                    {
                        positions[pos++] = i;
                        ti++;
                    }
                }
                best = positions;
            }

            from = windowStart + 1;
        }

        return best;
    }

    private static bool CharsEqual(char left, char right, bool caseSensitive)
        => caseSensitive ? left == right : char.ToLowerInvariant(left) == char.ToLowerInvariant(right);

    // A pinyin term spelled out over its own syllables needs a few characters of delimiter and padding per
    // syllable, which is what these bounds allow for. Both are the alias fallback's original measured
    // values; the sum-of-windows budget in FzfPatternAliasMatchExtensions is the multi-block counterpart.
    internal const int TightPerTermMultiplier = 4;
    internal const int TightPerTermFloor = 8;

    /// <summary>
    /// Whether a PRECISE alias match starting at <paramref name="matchStart"/> may stand.
    /// </summary>
    /// <remarks>
    /// The single decision used by both matching and highlighting, so the two cannot disagree -- a
    /// candidate that matched but lit nothing up (or the reverse) is the failure mode this guards against.
    /// <paramref name="precise"/> is false for a fuzzy term, where the user explicitly asked for a loose
    /// match and the boundary rule would contradict the operator they typed.
    /// </remarks>
    public static bool AllowsMatch(bool precise, char separator, ReadOnlySpan<char> alias, int matchStart)
        => !precise || IsBoundaryAligned(separator, alias, matchStart);

    /// <summary>Byte twin of <see cref="AllowsMatch(bool, char, ReadOnlySpan{char}, int)"/>.</summary>
    public static bool AllowsMatchUtf8(bool precise, char separator, ReadOnlySpan<byte> aliasUtf8, int matchStart)
        => !precise || IsBoundaryAligned(separator, aliasUtf8, matchStart);

    /// <summary>Convenience for a parsed pattern, whose term kinds decide whether the rule applies.</summary>
    public static bool AllowsMatch(FzfPattern pattern, char separator, ReadOnlySpan<char> alias, int matchStart)
        => AllowsMatch(pattern.RequiresAlignedAliases, separator, alias, matchStart);

    /// <inheritdoc cref="AllowsMatch(FzfPattern, char, ReadOnlySpan{char}, int)"/>
    public static bool AllowsMatchUtf8(FzfPattern pattern, char separator, ReadOnlySpan<byte> aliasUtf8, int matchStart)
        => AllowsMatchUtf8(pattern.RequiresAlignedAliases, separator, aliasUtf8, matchStart);

    /// <summary>
    /// How strong a match is by how it was found: the candidate's own name beats a within-word alias,
    /// which beats a full transliteration.
    /// </summary>
    /// <remarks>
    /// Derived from the matched text rather than recorded anywhere, so neither the index nor the snapshot
    /// format has to change: a provider's full transliteration is exactly the alias that carries its
    /// declared separator (every syllable boundary marked), while its shorthand/initialism alias carries
    /// none. So "did the matched alias contain the separator" is the whole test.
    ///
    /// Ordering intent, highest first: a literal hit (the query text really is in the name), then an
    /// initials hit ("ex" for 恶性 -- the user abbreviated), then a full-pinyin hit (the whole reading had
    /// to be spelled out). A provider with no declared structure is scored as shorthand, since it offers
    /// no way to tell the two apart.
    /// </remarks>
    public static int TierFor(char separator, bool matchedName, ReadOnlySpan<char> matchedAlias)
    {
        if (matchedName)
            return MatchRank.TierName;

        if (separator == '\0')
            return MatchRank.TierInitials;

        return matchedAlias.IndexOf(separator) >= 0 ? MatchRank.TierFull : MatchRank.TierInitials;
    }
}
