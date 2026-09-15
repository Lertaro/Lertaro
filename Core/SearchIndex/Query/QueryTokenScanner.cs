namespace Lertaro.Core.SearchIndex.Query;

// Extracts query tokens from ANYWHERE in a raw search query -- not just a trailing ":a,b,c" segment.
//
// A token is a whitespace-separated word whose first character is a token trigger:
//
//   \audio       a plugin token ("\" + the plugin's own key)
//   <s  <s>20m   sort/filter token ("<" or ">" + key [+ threshold])
//
// The triggers are characters that are illegal in a Windows file name, so a token can never be
// confused with text the user wants to search for. Everything that is not a token is left in place,
// in order, as the search text.
//
// Deliberately dumb: it has no idea what a token MEANS -- that is up to whichever IQueryTokenProvider
// plugin claims it (see QueryTokenDispatcher). It only decides the trigger characters, which is what
// lets a provider be added without touching this file.
//
// Quoting/escaping is preserved from the trailing-segment parser this replaces: a quoted word may
// contain whitespace, and "\ " escapes a space inside an unquoted word.
public static class QueryTokenScanner
{
    // The characters that start a token. '\' is the plugin-token prefix (GlobalTokenPrefix); '<' and
    // '>' are the sort/filter triggers. A word starting with any of these is pulled out; a word that
    // merely CONTAINS one is ordinary search text.
    public static ScanResult Scan(string query, char pluginPrefix = '\\')
    {
        if (string.IsNullOrWhiteSpace(query))
            return new ScanResult(query, Array.Empty<string>());

        var words = SplitWords(query);
        var tokens = new List<string>();
        var kept = new List<string>();

        foreach (var word in words)
        {
            // A quoted word is always search text -- quoting is how a user says "I mean these exact
            // characters", so "\audio" is matched literally rather than claimed as a token.
            if (IsQuoted(word))
            {
                kept.Add(word);
                continue;
            }

            if (IsToken(word, pluginPrefix))
                tokens.Add(Unquote(word));
            else
                kept.Add(word);
        }

        return new ScanResult(string.Join(' ', kept).Trim(), tokens);
    }

    private static bool IsToken(string word, char pluginPrefix)
    {
        if (word.Length < 2)
            return false;

        var first = word[0];
        return first == pluginPrefix || first == '<' || first == '>';
    }

    // Splits on unescaped whitespace, keeping quoted runs together. "\ " inside an unquoted word is a
    // literal space (not a separator), which is how a token can carry a space without quotes.
    private static List<string> SplitWords(string query)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        var activeQuote = '\0';

        for (var i = 0; i < query.Length; i++)
        {
            var c = query[i];

            if (activeQuote != '\0')
            {
                current.Append(c);
                if (c == activeQuote && !IsEscaped(query, i))
                    activeQuote = '\0';
                continue;
            }

            if (c == '"' || c == '\'')
            {
                activeQuote = c;
                current.Append(c);
                continue;
            }

            if (char.IsWhiteSpace(c) && !IsEscaped(query, i))
            {
                if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            // An escaped space is text, not a separator -- drop the backslash and keep the space.
            if (c == '\\' && i + 1 < query.Length && char.IsWhiteSpace(query[i + 1]))
            {
                current.Append(query[i + 1]);
                i++;
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            words.Add(current.ToString());
        return words;
    }

    private static bool IsQuoted(string word) =>
        word.Length >= 2 &&
        ((word[0] == '"' && word[^1] == '"') || (word[0] == '\'' && word[^1] == '\''));

    private static string Unquote(string word) => IsQuoted(word) ? word[1..^1] : word;

    private static bool IsEscaped(string text, int index)
    {
        var backslashCount = 0;
        for (var i = index - 1; i >= 0 && text[i] == '\\'; i--)
            backslashCount++;

        return backslashCount % 2 != 0;
    }

    // Strips a leading "*" -- the marker that opts one search out of the user's own exclusion rules
    // (see SearchService.SearchStreamingAsync's bypassExclusions parameter). Callers must run this
    // BEFORE the query is used for anything else (the actual search call, AND whatever gets stored as
    // an AppSearchResult's SearchQuery for highlighting) -- the character itself is never part of the
    // match/highlight text, only a query-string-level signal.
    //
    // It lives here, next to the token scan, because both are the same job: reducing raw typed text to
    // (search text + query-level signals). Unlike a token, "*" is a whole-query switch rather than a
    // term, so it is read from the first character only, not from anywhere in the string.
    public static string StripExclusionBypass(string query, out bool bypassExclusions)
    {
        bypassExclusions = query.Length > 0 && query[0] == '*';
        return bypassExclusions ? query[1..] : query;
    }
}

// The search text that remains after the tokens were lifted out, plus the tokens in the order they
// appeared. An empty Text with non-empty Tokens is a token-only query ("\audio" with no keyword yet).
public readonly record struct ScanResult(string Text, IReadOnlyList<string> Tokens);
