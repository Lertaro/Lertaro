using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.SearchIndex;

// One parsed query, reusable across many candidate texts -- the App-facing counterpart of FzfPattern
// (which stays internal to Core).
//
// Exists because the string-based FuzzyMatcher APIs re-parse the query on every call, and parsing is not
// cheap: it consults every registered alias provider's GetQueryForms, so with the pinyin provider loaded a
// single Parse measured roughly the same as ~5 plain matches. The per-candidate loops (a Start Menu /
// settings catalog, the history list, a result page scored by match rank) called those string APIs once
// or twice PER TEXT, which is where the per-keystroke lag came from. Parsing once and matching many times
// through this type removes all of it.
//
// The parsed pattern is immutable and safe to share; a new FuzzyQuery is created per search (per keystroke
// value), not cached process-wide, so a settings or provider change can never be served a stale parse.
public readonly struct FuzzyQuery
{
    private readonly FzfPattern? _pattern;
    private readonly string _text;

    private FuzzyQuery(FzfPattern pattern, string text)
    {
        _pattern = pattern;
        _text = text;
    }

    public static FuzzyQuery Parse(string? query)
        => string.IsNullOrEmpty(query) ? default : new FuzzyQuery(FzfPattern.Parse(query), query);

    // False for an empty/absent query, so a caller can short-circuit a whole candidate loop.
    public bool IsEmpty => _pattern is null;

    // The query text this was parsed from, for the callers that carry it onto a result row (display,
    // highlighting, history recording). Empty when the query was empty.
    public string Text => _text ?? string.Empty;

    public bool IsMatch(string? text)
        => _pattern is not null && !string.IsNullOrEmpty(text) && FuzzyMatcher.IsMatch(_pattern, text);

    /// <summary>
    /// Whether <paramref name="text"/> matches through a pinyin reading of it -- an initials query against a
    /// han-character name, which <see cref="IsMatch"/> cannot see because it compares characters.
    /// </summary>
    /// <remarks>
    /// Exists for the inline window's direct-children listing: those rows come from a filesystem enumeration
    /// rather than from the index, so they carry no baked alias and the alias-aware matcher the engine uses
    /// never applies to them. Their names are still han characters, so an initials query returned nothing from
    /// the folder the user was looking at while the same query worked in the global results.
    ///
    /// The evaluator is cached per pattern (see <see cref="IndexV2.Search.PinyinNameEvaluator.ForPattern"/>),
    /// so this costs one dictionary lookup per listing rather than one per candidate.
    /// </remarks>
    public bool IsAliasMatch(string? text)
        => _pattern is not null
           && !string.IsNullOrEmpty(text)
           && IndexV2.Search.PinyinNameEvaluator.ForPattern(_pattern).TryScore(text, null, out _);

    // The shared ranking measure (start position + quality weight). NoMatch for an empty query or text.
    public MatchRank Rank(string? text)
        => _pattern is null || string.IsNullOrEmpty(text) ? MatchRank.NoMatch : FuzzyMatcher.ComputeMatchRank(text, _pattern);

    // The rank for a candidate whose own name did NOT match the query at all -- it was reached through a
    // transliteration of that name, or by an ancestor folder completing the query. See
    // MatchRank.NoNameFallback: the full window's row key has always used this rank, and the quick window
    // calls it for the same rows so the two windows cannot order them differently.
    public MatchRank FallbackRank(string? path)
        => _pattern is null || string.IsNullOrEmpty(path)
            ? MatchRank.NoMatch
            : MatchRank.NoNameFallback(FuzzyMatcher.ComputeMatchWeight(path, _pattern));

    // Best match across the item's several text representations -- see FuzzyMatcher.ComputeBestMatch.
    public MatchRank BestMatch(string? primaryText, IEnumerable<string>? alternateTexts = null)
        => _pattern is null ? MatchRank.NoMatch : FuzzyMatcher.ComputeBestMatch(_pattern, primaryText, alternateTexts);

    public bool[] HighlightMask(string? text)
        => _pattern is null || string.IsNullOrEmpty(text) ? Array.Empty<bool>() : FuzzyMatcher.ComputeHighlightMask(text, _pattern);
}
