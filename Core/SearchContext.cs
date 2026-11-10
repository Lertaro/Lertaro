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
    /// The "/.../" clauses in <paramref name="query"/> that the regex engine cannot compile, in the order
    /// they appear, so a caller can tell the user which part of their query could never match anything.
    /// Empty when every clause compiles.
    /// </summary>
    /// <remarks>
    /// A pure function of the query text, and that is the whole point. This used to be a process-wide list
    /// that compiling wrote into as a side effect, which could not answer "what is wrong with THIS query":
    /// a clause already in the compile cache reported nothing, so the hint disappeared the second time the
    /// user typed it; a superseded search still running reported ITS clauses into the shared list; and the
    /// clauses for an indexed local drive are compiled in the elevated service process, so the App's copy
    /// stayed empty on the common path. Whether a pattern compiles depends only on its text, so asking is
    /// cheaper than remembering, and works from either process.
    ///
    /// Takes the query as typed. Regex clauses survive token lifting and the '*' bypass marker, so a clause
    /// named here is one the search will also have seen.
    /// </remarks>
    public static IReadOnlyList<string> UncompilableClauses(string? query)
    {
        if (string.IsNullOrEmpty(query))
            return Array.Empty<string>();

        SearchIndex.Query.RegexQueryParser.Split(query, out var patterns);
        if (patterns is not { Length: > 0 })
            return Array.Empty<string>();

        List<string>? invalid = null;
        foreach (var pattern in patterns)
        {
            if (SearchIndex.Fzf.RegexClauses.IsUncompilable(pattern))
                (invalid ??= []).Add(pattern);
        }

        return invalid ?? (IReadOnlyList<string>)Array.Empty<string>();
    }
}
