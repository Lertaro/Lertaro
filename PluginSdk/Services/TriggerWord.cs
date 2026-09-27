namespace Lertaro.PluginSdk.Services;

/// <summary>
/// The single rule for "the leading word the user typed to invoke something" -- <c>set 路径</c> is the
/// word <c>set</c> carrying the argument <c>路径</c>. Both halves of a trigger word used to decide this
/// separately: the host cut the word off the file-name search with one set of rules
/// (<c>PluginTriggerQuery</c>), an action dispatched on another (<c>KeywordMatcher</c>), a scope keyword
/// on a third, and every instant provider re-recognised its own word with a fourth copy of
/// <c>StartsWith(word + " ")</c>. The copies drifted, so a word could be stripped by the host while its
/// owner failed to recognise it -- the user then has neither the feature nor their search text.
/// </summary>
/// <remarks>
/// Separators are any Unicode whitespace (<see cref="char.IsWhiteSpace(char)"/>), which covers the
/// ideographic space U+3000 a Chinese IME emits in full-width mode and a pasted non-breaking space --
/// neither of which any copy handled before. Consecutive separators count as one and the argument comes
/// back trimmed, so <c>cs&nbsp;&nbsp;report</c> and <c>cs　report</c> both mean <c>cs report</c>.
///
/// What is deliberately NOT decided here is whether a word with nothing after it activates. The host must
/// not strip one (that word is all the user has asked to find so far); a window list or a command does
/// activate bare; a provider whose bare form would flood the list with placeholder rows does not. Pick
/// <see cref="TryMatch"/> or <see cref="TryMatchInvoked"/> accordingly.
/// </remarks>
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
