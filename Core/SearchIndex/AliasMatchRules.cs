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
        var previous = alias[matchStart - 1];
        return previous == separator || previous == '|';
    }

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

        // '|' as well as the separator: see the char overload.
        var previous = aliasUtf8[matchStart - 1];
        return previous == separatorByte || previous == (byte)'|';
    }

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
