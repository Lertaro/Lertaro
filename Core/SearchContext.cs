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
}
