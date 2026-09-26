namespace Lertaro.Core.SearchIndex.Fzf;

// The characters a term's FIRST character is read as instead of as text, and what each one does to it.
//
// One reading, in one place, because every entry point that parses a user-written query has to agree on it:
// the search box, the windows that filter their own candidates (the quick panel's box, the shell-menu
// filter, the settings search/log/history/plugin boxes), the plugin catalog scan, and the FuzzyMatcher
// seam the plugins and the CLI pipe call -- all of them reach FzfPatternParser.
//
// They did not always. FuzzyMatcher's seam used to carry its own, older operator set ('!' "'" '^' '$') that
// the search box had already dropped, so the same typed text meant two different queries depending on which
// window asked: "!temp" excluded temp from a catalog but searched for a literal "!temp" in the file list,
// and "^read" anchored a prefix in one place while the other looked for a caret. All four are ordinary
// characters again, and that duplication is why the reading below exists once rather than twice.
//
// Only the FIRST character of a word is ever a trigger. "rep?ort" is the literal text "rep?ort", which can
// never match anything: '?' cannot occur in a Windows file name.
internal static class TermTriggers
{
    // The exclusion operator: ":term" drops every candidate whose file name contains "term". It shares the
    // colon with the drive spec -- but the two can never collide, because a drive spec is the whole token
    // "d:" (letter THEN colon, split off in FzfPatternParser.Parse) while an exclusion is the colon FIRST.
    // A colon inside a word, as in "c:\path", is not a leading character.
    internal const char Exclusion = ':';

    // The precision-inversion operator: a leading '?' flips the word between fuzzy and exact, the opposite
    // of whatever the fuzzy-matching setting says. It covers the case that setting cannot express -- one
    // term out of a query -- and is the successor to the historical leading "'" flip.
    internal const char PrecisionInversion = '?';

    /// <summary>
    /// Reads whatever trigger this word carries: the body to match (the trigger stripped), the term kind it
    /// takes, and whether it is an exclusion. A word that is nothing but a trigger yields an empty body,
    /// which the caller must discard -- exactly as a lone ":" or "?" has always behaved.
    /// </summary>
    /// <remarks>
    /// ':' is read first, so ":?temp" excludes the literal text "?temp" rather than inverting an exclusion:
    /// an exclusion is already pinned to Exact, and a user who typed the colon first was writing a name,
    /// not an operator.
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

        var kind = ReadKind(token, out var body);
        return (body, kind, Inverse: false);
    }

    // Fuzzy when the setting says so and Exact otherwise, flipped when the word carries the precision
    // trigger. The flip is Fuzzy <-> Exact and nothing else -- those are the only two readings the setting
    // itself chooses between, so an anchored kind can never be its input, and this is the only place the
    // flip exists.
    private static FzfTermKind ReadKind(string token, out string body)
    {
        var kind = SearchContext.FuzzyMatchEnabled ? FzfTermKind.Fuzzy : FzfTermKind.Exact;
        if (token.Length == 0 || token[0] != PrecisionInversion)
        {
            body = token;
            return kind;
        }

        body = token[1..];
        return kind == FzfTermKind.Fuzzy ? FzfTermKind.Exact : FzfTermKind.Fuzzy;
    }
}
