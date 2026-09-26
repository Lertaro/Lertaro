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
// The one carve-out is a word that opens with the plugin prefix TWICE: that is a UNC path
// ("\\server\share"), which a separator DOES build, and no keyword can start with '\' either.
//
// Deliberately dumb: it has no idea what a token MEANS -- that is up to whichever IQueryTokenProvider
// plugin claims it (see QueryTokenDispatcher). It only decides the trigger characters, which is what
// lets a provider be added without touching this file.
//
// Nothing else here is syntax. Quoting came from the trailing-segment parser this replaces and went with
// the rest of that grammar, so '"' and '\'' are ordinary characters now; the only escape left is "\ " so a
// keyword can carry a space.
public static class QueryTokenScanner
{
    // The characters that start a token. '\' is the plugin-token prefix (GlobalTokenPrefix); '<' and
    // '>' are the sort/filter triggers. A word starting with any of these is pulled out; a word that
    // merely CONTAINS one is ordinary search text -- as is a UNC path, which starts with two of them.
    public static ScanResult Scan(string query, char pluginPrefix = '\\')
    {
        if (string.IsNullOrWhiteSpace(query))
            return new ScanResult(query, Array.Empty<string>());

        var words = SplitWords(query);
        var tokens = new List<string>();
        var kept = new List<string>();

        foreach (var word in words)
        {
            if (IsToken(word, pluginPrefix))
                tokens.Add(word);
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
        if (first == pluginPrefix)
            // "\\" is how a UNC path opens. A token is the prefix + a keyword, and '\' cannot start a
            // keyword (it is illegal in a Windows file name), so a doubled prefix is never one.
            return word[1] != pluginPrefix;

        return first == '<' || first == '>';
    }

    // Splits on unescaped whitespace. "\ " inside a word is a literal space (not a separator), which is
    // how a token can carry a space.
    private static List<string> SplitWords(string query)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();

        for (var i = 0; i < query.Length; i++)
        {
            var c = query[i];

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
    // That includes running it before Scan: the scanner reads a token trigger from a word's FIRST
    // character, so a marker glued to one ("*\audio") would leave the word looking like ordinary text and
    // the token would never be dispatched at all.
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
