namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Optional capability of an <see cref="IAliasProvider"/>: exposes its aliases as one set of readings
/// per source position instead of only as whole alias strings, so the host can bake and store a name's
/// entire alias space in space linear in the name's length.
/// </summary>
/// <remarks>
/// A provider's alias space is the cross product of its per-position readings: a name of n characters
/// with k readings each has k^n spellings. Any encoding that materializes whole strings has to bound
/// that enumeration somewhere, and a bound that cuts the enumeration off is what silently dropped a
/// whole alias set in the pinyin provider (see PinyinAliasCombinationGenerator). The host does not
/// enumerate either: it stores the readings and decides membership by walking a query term over them.
///
/// The host owns the storage encoding and the matching algorithm (<c>AliasGridFormat</c> /
/// <c>AliasGridMatcher</c> in Core), so one format serves every provider and a provider never has to
/// understand the host's storage. A provider supplies the two things the host cannot know: which
/// readings each position has, and where that provider's syllable boundaries fall.
///
/// Implementations must agree with <see cref="IAliasProvider.GetAliases"/> for the same text, because
/// the two are alternative encodings of one alias space and the host picks between them per name:
/// the initials alias is one character per position, each the first character of one of that
/// position's readings; the full alias is those readings joined, with
/// <see cref="IAliasProvider.SyllableSeparator"/> inserted where <see cref="NeedsSeparatorBefore"/>
/// says a boundary belongs. A name whose readings disagree between the two encodings would be found by
/// a baked index and missed by a live scan, or the reverse.
/// </remarks>
public interface IAliasGridProvider : IAliasProvider
{
    /// <summary>
    /// One entry per character of <paramref name="text"/>, each holding that character's candidate
    /// readings in this provider's own spelling (pinyin syllables for the pinyin provider).
    /// </summary>
    /// <remarks>
    /// A character with no reading of its own -- ASCII, punctuation, an unmapped script -- still gets
    /// an entry, holding its literal lowercased text, because that is what it contributes to an alias.
    /// Entries are never empty and never contain an empty reading.
    ///
    /// Only the first <c>text.Length</c> entries are meaningful; the array may be a larger reused
    /// scratch whose remaining entries are stale or null, so callers iterate it by the text's length
    /// rather than by the array's.
    ///
    /// Lifetime: the returned array may be the provider's own reuse scratch, valid only until the
    /// provider's next call on the same thread. The host reads it and copies what it needs before
    /// calling back in.
    /// </remarks>
    string[][] GetReadingSets(string text);

    /// <summary>
    /// Whether position <paramref name="index"/>'s reading is preceded by
    /// <see cref="IAliasProvider.SyllableSeparator"/> in a full alias.
    /// </summary>
    /// <remarks>
    /// Never consulted for the initials alias, which is one character per position and has no internal
    /// structure to protect.
    /// </remarks>
    bool NeedsSeparatorBefore(string text, int index);

    /// <summary>
    /// Writes the aliases that spell out <see cref="GetReadingSets"/> -- the ones the host may replace
    /// with the grid -- in the same order and shape <see cref="IAliasProvider.GetAliasesUtf8"/> emits
    /// them.
    /// </summary>
    void GetReadingAliasesUtf8(string text, AliasByteSink dest);

    /// <summary>
    /// Writes this provider's aliases that do <em>not</em> spell out the reading sets. The host keeps
    /// these next to the grid, because the grid only replaces the reading-derived ones.
    /// </summary>
    /// <remarks>
    /// The split exists so the host can weigh the two encodings of the reading space against each other
    /// and drop only the loser. Together the two methods must emit exactly what
    /// <see cref="IAliasProvider.GetAliasesUtf8"/> emits, in that order; the provider's own tests hold
    /// that identity, because a provider whose two halves disagree would bake an alias set that no
    /// longer matches what its string API advertises.
    /// </remarks>
    void GetStandaloneAliasesUtf8(string text, AliasByteSink dest);
}
