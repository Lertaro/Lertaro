namespace Lertaro.Plugins.PinyinAlias;

// The one place that owns the shape of a generated full-pinyin spelling. The spelling materialization
// (PinyinAliasCombinationGenerator) and the host's alias-side reading (PinyinAliasProvider's
// MapAliasToSourceIndices, which has to consume a boundary exactly where it was written) both take the
// delimiter from here.
internal static class PinyinAliasFormat
{
    // Syllable delimiter for the full-pinyin spelling. A control character for the same reason
    // PinyinAliasCombinationGenerator's PipePlaceholder is one: it cannot occur in a real name, so it
    // never collides with source text that already passes through verbatim (adjacent transliterated
    // characters are split -- "的士" becomes "di<SEP>shi" -- while digits and dots pass through intact),
    // and it cannot occur in anything a user types,
    // so no query can straddle it the way "n.z" could straddle a printable '.' separator.
    //
    // It also costs nothing on the hot path: being below 128 it keeps the alias pure ASCII, so the
    // byte-native matcher (Ascii.IsValid -> FzfBytePattern) still applies.
    //
    // Only the FULL-pinyin spelling carries it. The initials spelling is one character per source
    // character, so every position in it is already a boundary -- and the host detects that alias by its
    // length matching the source text (see PinyinAliasProvider.MapAliasToSourceIndices), which a
    // delimiter would break.
    public const char SyllableSeparator = (char)2;

    /// <summary>
    /// Whether a syllable boundary belongs before the syllable produced by <paramref name="index"/>:
    /// between two adjacent characters that were both transliterated. Every path that writes a full
    /// spelling uses it, and <c>MapAliasToSourceIndices</c> consumes what it wrote.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT emitted where a transliterated character meets a passed-through one. A name
    /// like "第01集.mp4" keeps its alias as "di01ji.mp4", so typing the digits you can see followed by
    /// pinyin ("01ji") still works; separating there would break that for no gain, since the digits
    /// already are a visible boundary rather than a run of letters something can hide inside.
    ///
    /// The test is the table lookup itself, not "is non-ASCII": an emoji or any other unmapped
    /// non-ASCII character passes through literally exactly like an ASCII one, and treating it as a
    /// syllable put a boundary in the middle of text that never had one.
    /// </remarks>
    public static bool NeedsSeparatorBefore(string text, int index)
        => index > 0
           && PinyinEngine.TryGetPinyins(text[index], out _)
           && PinyinEngine.TryGetPinyins(text[index - 1], out _);
}
