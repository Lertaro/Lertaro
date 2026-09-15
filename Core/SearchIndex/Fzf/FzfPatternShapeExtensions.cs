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

    // A pattern built from term sets a caller produced itself (FuzzyMatcher's operator-compatible entry
    // point), for callers that have already parsed their own term list rather than a query string. The
    // caller owns the shape: sets are ANDed and the terms inside one set are OR alternatives, so a
    // space-separated query must hand in one set PER word -- one set holding every word would silently
    // read as "any of these words" instead of "all of them".
    public static FzfPattern FromTermSets(FzfTermSet[] sets)
        => new(null, sets);
}
