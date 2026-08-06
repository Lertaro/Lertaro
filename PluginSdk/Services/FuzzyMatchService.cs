namespace Lertaro.PluginSdk.Services;

/// <summary>
/// A decoupled service exposing the host's own fuzzy-match engine (the same matching used for the
/// primary search, including its alias/transliteration fallback) to plugins that need identical
/// matching semantics without reimplementing a fuzzy matcher of their own.
/// </summary>
public static class FuzzyMatchService
{
    /// <summary>
    /// Delegate set by the host application. Parameters: (pattern, text) -- returns whether
    /// <paramref name="text"/>, or one of its generated aliases, matches the fzf-syntax
    /// <paramref name="pattern"/>.
    /// </summary>
    public static Func<string, string, bool>? IsMatchFunc { get; set; }

    /// <summary>
    /// Returns whether <paramref name="text"/> matches the fzf-syntax <paramref name="pattern"/>,
    /// using the exact same matching (and alias fallback) rule the host's own search uses.
    /// </summary>
    public static bool IsMatch(string pattern, string text) => IsMatchFunc?.Invoke(pattern, text) ?? false;

    /// <summary>
    /// Delegate set by the host application. Parameters: (text, query) -- returns the per-character
    /// highlight mask the host's own display highlighting uses for that pair.
    /// </summary>
    public static Func<string, string, bool[]>? GetHighlightMaskFunc { get; set; }

    /// <summary>
    /// Returns the highlight mask for <paramref name="text"/> against <paramref name="query"/>, using
    /// the exact same literal/fuzzy/alias fallback tiers (including CJK pinyin matching) the host's own
    /// search results use -- so a plugin's own results highlight consistently with everything else
    /// instead of only handling a literal substring match.
    /// </summary>
    public static bool[]? GetHighlightMask(string text, string query) => GetHighlightMaskFunc?.Invoke(text, query);
}
