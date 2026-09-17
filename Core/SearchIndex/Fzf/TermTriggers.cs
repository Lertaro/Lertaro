namespace Lertaro.Core.SearchIndex.Fzf;

// The characters a term's FIRST character is read as instead of as text, and what each one does to it.
//
// Shared, because two parsers read a user-written query and must agree on it: FzfPatternParser, behind the
// search box, and FuzzyMatcher's raw-string seam, behind the plugin catalog, bookmark titles, the shell
// menu filter and the settings search boxes. Nothing puts a search pipeline in front of that second set, so
// the string it receives is still the raw query -- and a trigger read on one path but not the other makes
// the same typed query answer differently in two windows of the same app (the catalog rows would simply
// disappear from a search the file list still honours).
//
// Only the FIRST character of a word is ever a trigger. "rep?ort" is the literal text "rep?ort", which can
// never match anything -- '?' cannot occur in a Windows file name -- the same fate the old "'" had there.
//
// The historical set is otherwise gone: '!' "'" '^' '$' mean nothing now and are searched as literal
// characters, as is a trailing "'" ("'word'" is no longer the boundary form it used to be).
internal static class TermTriggers
{
    // The exclusion operator: ":term" drops every candidate whose file name contains "term". It replaced
    // the old '!', and it shares the colon with the drive spec -- but the two can never collide, because a
    // drive spec is the whole token "d:" (letter THEN colon, split off in FzfPatternParser.Parse) while an
    // exclusion is the colon FIRST. A colon inside a word, as in "c:\path", is not a leading character.
    internal const char Exclusion = ':';

    // The precision-inversion operator: a leading '?' flips the word between fuzzy and exact, the opposite
    // of whatever the fuzzy-matching setting says. It covers the case that setting cannot express -- one
    // term out of a query -- and is the successor to the historical leading "'" flip.
    internal const char PrecisionInversion = '?';

    /// <summary>
    /// The full reading the search pipeline uses: both triggers, plus the term kind and whether the word is
    /// an exclusion. Returns the body to match (the trigger stripped) and, for an empty body, a word the
    /// caller must discard -- exactly as a lone ":" or "?" behaves.
    /// </summary>
    /// <remarks>
    /// ':' is read first, so ":?temp" excludes the literal text "?temp" rather than inverting an exclusion:
    /// an exclusion is already pinned to Exact below, and a user who typed the colon first was writing a
    /// name, not an operator.
    /// </remarks>
    internal static (string Body, FzfTermKind Kind, bool Inverse) Read(string token)
    {
        if (token.Length > 0 && token[0] == Exclusion)
        {
            // Always Exact, whatever the fuzzy setting says. The old '!' worked the same way: an exclusion
            // is a statement about what must NOT be there, so a loose subsequence reading would reject far
            // more than the user named.
            return (token[1..], FzfTermKind.Exact, Inverse: true);
        }

        var (body, kind) = ReadPrecision(token);
        return (body, kind, Inverse: false);
    }

    /// <summary>
    /// The precision trigger alone, for the raw-string seam (see the type header) -- that entry point has
    /// never read ':'. The body is the token unchanged and the kind is the one the fuzzy-matching setting
    /// chose, unless '?' is present: then the body loses its leading character and the kind becomes the
    /// other of the two.
    /// </summary>
    /// <remarks>
    /// The flip is Fuzzy &lt;-&gt; Exact and nothing else. Those are the only two readings the setting itself
    /// chooses between, so an anchored kind (Prefix, Suffix, Equal, ExactBoundary) can never be its input;
    /// a caller that reads further operators may still override the kind afterwards.
    /// </remarks>
    internal static (string Body, FzfTermKind Kind) ReadPrecision(string token)
    {
        var kind = SearchContext.FuzzyMatchEnabled ? FzfTermKind.Fuzzy : FzfTermKind.Exact;

        if (token.Length == 0 || token[0] != PrecisionInversion)
            return (token, kind);

        return (token[1..], kind == FzfTermKind.Fuzzy ? FzfTermKind.Exact : FzfTermKind.Fuzzy);
    }
}
