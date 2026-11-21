namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Parses leading trigger words consistently for host stripping, actions, scopes, and instant providers.
/// For example, "set 路径" supplies trigger "set" and argument "路径".
///
/// Accept all Unicode whitespace, including U+3000 and non-breaking spaces. Collapse separators and trim
/// the argument. Callers choose whether a bare trigger activates through TryMatch or TryMatchInvoked; the
/// host preserves bare words for file search.
/// </summary>
public static class TriggerWord
{
    /// <summary>
    /// A word as stored in a setting, brought to the form every comparison below expects: surrounding
    /// whitespace off, empty for null or whitespace-only. The host trims the word it strips, so a provider
    /// that compares against the untrimmed value recognises nothing while the host still removes the word
    /// from the file search -- normalising on read closes that hole wherever a word is configured.
    /// </summary>
    public static string Normalize(string? configured) => configured?.Trim() ?? string.Empty;

    /// <summary>
    /// Whether <paramref name="query"/>'s first token is exactly <paramref name="word"/> (case-insensitive,
    /// like every other keyword comparison in the search box), with <paramref name="argument"/> set to the
    /// remaining trimmed text -- empty when the query holds nothing but the word itself, which is still a
    /// match. Returns false when a longer word merely starts with it (<c>csreport</c> is not <c>cs</c>).
    /// </summary>
    public static bool TryMatch(string query, string? word, out string argument)
        => TryMatchCore(query, word, out argument, out _);

    /// <summary>
    /// As <see cref="TryMatch"/>, but the word only counts as invoked once something has actually been
    /// typed after it -- <c>cs</c> alone is still a legitimate search for files called "cs", while
    /// <c>cs </c> is the provider being asked for its browse-all or placeholder view. Use this for a word
    /// whose bare form would put rows on screen the user did not ask for.
    /// </summary>
    public static bool TryMatchInvoked(string query, string? word, out string argument)
        => TryMatchCore(query, word, out argument, out var followedBySeparator) && followedBySeparator;

    /// <summary>
    /// As <see cref="TryMatch"/>, against a list of words -- the first one that matches wins, in the order
    /// given (registration order for the host, source-list order for a web-search engine per keyword). Also
    /// reports which word matched, since callers show or re-parse against that word.
    /// </summary>
    public static bool TryMatchAny(string query, IReadOnlyList<string> words, out string matchedWord, out string argument)
    {
        matchedWord = string.Empty;
        argument = string.Empty;
        for (var i = 0; i < words.Count; i++)
        {
            if (!TryMatch(query, words[i], out argument))
                continue;
            matchedWord = Normalize(words[i]);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Whether <paramref name="query"/> is a still-unfinished typing of <paramref name="word"/> ("m" on the
    /// way to "mkdir") -- the one branch that may offer a trigger before the whole word is there. False
    /// once any separator has been typed, because at that point the first token is final and either equals
    /// the word or does not.
    /// </summary>
    public static bool IsTypedPrefixOf(string query, string? word)
    {
        var text = Normalize(query);
        var key = Normalize(word);
        return text.Length > 0 && text.Length < key.Length
            && !HasSeparator(text)
            && key.StartsWith(text, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryMatchCore(string query, string? word, out string argument, out bool followedBySeparator)
    {
        argument = string.Empty;
        followedBySeparator = false;
        var key = Normalize(word);
        if (string.IsNullOrEmpty(query) || key.Length == 0)
            return false;

        // Leading whitespace carries no meaning in the search box, so it is not what separates the word
        // from its argument either -- "  cs report" invokes "cs" just as "cs report" does.
        var text = query.TrimStart();
        if (text.Length < key.Length || !text[..key.Length].Equals(key, StringComparison.OrdinalIgnoreCase))
            return false;

        if (text.Length == key.Length)
            return true;

        if (!char.IsWhiteSpace(text[key.Length]))
            return false;

        followedBySeparator = true;
        argument = text[key.Length..].Trim();
        return true;
    }

    private static bool HasSeparator(string text)
    {
        foreach (var c in text)
            if (char.IsWhiteSpace(c))
                return true;
        return false;
    }
}
