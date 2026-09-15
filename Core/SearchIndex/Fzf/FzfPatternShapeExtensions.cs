namespace Lertaro.Core.SearchIndex.Fzf;

// Shape queries over an already-parsed FzfPattern plus the handful of pattern FACTORIES callers need,
// split out (composition as extension methods -- not a partial class) to keep FzfPattern.cs under the
// project's line limit. FzfPattern keeps the pattern state and the core text-matching algorithm; this
// file holds the questions a call site asks about that state before deciding how to match. The shape
// queries read only what FzfPattern already publishes; the factories build a pattern from terms a caller
// produced itself, which is not a query-string parse and so does not belong on FzfPatternParser either.
internal static class FzfPatternShapeExtensions
{
    // A pattern with nothing to match on: the shared "no terms" value for a parser that bailed out, so
    // callers can hand back a pattern in an out-parameter without each of them allocating its own.
    public static FzfPattern Empty { get; } = new(null, Array.Empty<FzfTermSet>());

    // A pattern built from terms a caller produced itself (FuzzyMatcher's operator-compatible entry
    // point), for the callers that have a term list rather than a query string to parse. All terms go
    // into ONE term set, which is the flat AND reading -- there is no '|' in an operator grammar.
    public static FzfPattern FromTerms(FzfTerm[] terms)
        => new(null, new[] { new FzfTermSet(terms) });

    // True when the query's text was entirely regex (no ordinary terms), which is the expensive case: no
    // literal term exists for the index prefilter to consume, so the regex falls back to a full scan.
    public static bool IsRegexOnly(this FzfPattern pattern)
        => pattern.Regexes is { Length: > 0 } && pattern.TermSets.Length == 0;
}
