namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Optional capability of an <see cref="IAliasProvider"/>: decides a query term against a candidate
/// text LIVE, one character at a time, from the provider's own reading table -- the index bakes
/// nothing for this provider's readings at all (no alias strings, no payloads).
/// </summary>
/// <remarks>
/// The matching semantics come from WinShun's pinyin matcher, ported wholesale: every character of
/// the text must consume at least one letter of the term -- an ASCII character matches itself (case
/// folded), a Han character consumes any NON-EMPTY PREFIX of any of its readings (its initial, the
/// whole syllable, or a half-typed run, anywhere in the term). A matched span is therefore contiguous
/// (no character inside it is skipped), a term is 2..63 ASCII letters/digits with at least one letter
/// (single letters are not this provider's words), 'v' (ü) also accepts 'u', and terms are matched
/// independently of one another. The host owns scoring, ranking and highlighting; this interface only
/// answers whether a term occurs and where, plus the two things the host cannot know: the
/// per-position readings (for the bake-time prefilter mask) and the single-reading spellings the
/// mixed-alphabet lane matches as text.
/// </remarks>
public interface IAliasLiveMatcherProvider : IAliasProvider
{
    /// <summary>
    /// Builds the matcher for one folded query term, for the whole query (one matcher serves every
    /// candidate; implementations must be thread-safe and allocation-free per <see
    /// cref="IAliasTermMatcher.TryFind"/> call). Returns null when the term is not one of this
    /// provider's words (caller falls back to literal matching for it).
    /// </summary>
    /// <param name="commonOnly">
    /// Judge each character by its most common reading only. The host runs both matchers for a
    /// matching term; the common-only one finding it is what says the term needed no rare reading.
    /// </param>
    IAliasTermMatcher? CreateMatcher(string foldedTerm, bool commonOnly);

    /// <summary>
    /// The candidate readings of each character of <paramref name="text"/> in this provider's own
    /// spelling, for indices [0, text.Length). Used ONLY by the bake-time prefilter mask; matching
    /// itself goes through <see cref="CreateMatcher"/>.
    /// </summary>
    /// <remarks>
    /// The returned array is the provider's reusable scratch: it may be LONGER than the text (with
    /// entries beyond <c>text.Length</c> left over from an earlier, longer call), and it, its entries and
    /// the strings inside are only valid until the next call on the same thread. Copy what you need before
    /// calling back into the provider.
    /// </remarks>
    string[][] GetReadingSets(string text);

    /// <summary>
    /// Appends the spellings of <paramref name="text"/> built from each position's FIRST reading --
    /// the initials spelling, then the full spelling with syllable separators -- for the
    /// mixed-alphabet lane, which matches an alias as text and maps the positions back onto the
    /// source. May append nothing when the text carries no readings.
    /// </summary>
    void AppendSpellings(string text, List<string> dest);
}

/// <summary>
/// One query term, decided against a candidate text: whether the term occurs (by this provider's
/// reading semantics) and where. Immutable after construction; safe to share across search threads.
/// </summary>
public interface IAliasTermMatcher
{
    /// <summary>
    /// The leftmost match of the term in <paramref name="text"/> (UTF-16 code units). On true,
    /// <paramref name="start"/>/<paramref name="end"/> delimit the contiguous matched span and --
    /// when <paramref name="painted"/> is non-empty and sized to the text -- every character of the
    /// span, which is exactly the set the walk consumed, is marked.
    /// </summary>
    bool TryFind(ReadOnlySpan<char> text, Span<bool> painted, out int start, out int end);
}
