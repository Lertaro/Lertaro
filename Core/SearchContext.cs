using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core;

public static class SearchContext
{
    private static readonly AsyncLocal<HashSet<byte>?> _disabledAliasIds = new();

    public static HashSet<byte>? DisabledAliasIds
    {
        get => _disabledAliasIds.Value;
        set => _disabledAliasIds.Value = value;
    }

    private static readonly AsyncLocal<bool?> _fuzzyMatchEnabled = new();
    private static volatile bool _defaultFuzzyMatchEnabled = true;

    // Process-wide fallback for the many FzfPattern parses that happen OUTSIDE a search request and so
    // never see the per-request value: the plugin catalog, favorites, shell-menu filtering, and display
    // highlighting all match on their own call paths, and an AsyncLocal set inside the search pipeline
    // does not flow to any of them. The app pushes the user's preference here at startup and whenever
    // settings are saved; the service leaves it alone (it has no user settings to read, and always sets
    // the per-request value explicitly), so it stays at the historical fuzzy default there.
    public static bool DefaultFuzzyMatchEnabled
    {
        get => _defaultFuzzyMatchEnabled;
        set => _defaultFuzzyMatchEnabled = value;
    }

    public static bool FuzzyMatchEnabled
    {
        get => _fuzzyMatchEnabled.Value ?? _defaultFuzzyMatchEnabled;
        set => _fuzzyMatchEnabled.Value = value;
    }

    private static readonly AsyncLocal<bool?> _andFirstPrecedence = new();
    private static volatile bool _defaultAndFirstPrecedence = true;

    // Same two-tier shape as FuzzyMatchEnabled above, and for the same reason: pattern parsing also
    // happens outside a live search request (plugin catalog, favorites, shell-menu filtering, display
    // highlighting), so those call paths can only see the process-wide value the app pushes.
    //
    // true (the default) binds a bare space as AND and `|` as OR in the usual "AND tighter" order:
    // "report | summary 2024" is report OR (summary AND 2024). false restores the historical OR-first
    // reading -- (report OR summary) AND 2024 -- which is what every pre-existing saved settings file
    // and every request from a service-less caller keeps landing on.
    public static bool DefaultAndFirstPrecedence
    {
        get => _defaultAndFirstPrecedence;
        set => _defaultAndFirstPrecedence = value;
    }

    public static bool AndFirstPrecedence
    {
        get => _andFirstPrecedence.Value ?? _defaultAndFirstPrecedence;
        set => _andFirstPrecedence.Value = value;
    }

    /// <summary>
    /// The "/.../" clauses the most recent search could not compile, in the order they were first seen, so
    /// a caller can tell the user which part of their query could never match anything. Empty when every
    /// clause compiled.
    /// </summary>
    /// <remarks>
    /// Lives here rather than on the regex machinery because that is internal to Core while this report is
    /// for the UI, and this is already the static channel the app reads process-wide search state from
    /// (<see cref="DefaultFuzzyMatchEnabled"/>). An uncompilable clause is otherwise indistinguishable from
    /// a genuine miss: it matches nothing, so a query mixing one with ordinary words returns no results
    /// while the only explanation on screen says the search was too narrow.
    ///
    /// Callers that DISPLAY this are expected to call <see cref="ClearInvalidRegexes"/> once they have,
    /// so a clause the user has since fixed or deleted cannot be reported again on the next search.
    /// </remarks>
    public static IReadOnlyList<string> InvalidRegexes => RegexClauses.InvalidPatterns;

    /// <summary>Forgets the collected invalid clauses -- see <see cref="InvalidRegexes"/>.</summary>
    public static void ClearInvalidRegexes() => RegexClauses.ClearInvalidPatterns();
}
