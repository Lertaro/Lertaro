namespace Lertaro.Core.SearchIndex.Query;

// Pulls the "must appear verbatim" pieces out of a regular expression so a regex search can be
// pre-filtered the same way a literal one is.
//
// Why this exists: the index prefilter works on literal substrings (a candidate must contain every
// required literal, which is checkable with a character bitmask). A regex has no such mask, so running
// it against every indexed name means executing a regex engine millions of times -- roughly three
// orders of magnitude more expensive than the literal path. Extracting a literal that any match must
// contain lets the existing cheap prefilter run first, and only the survivors reach the regex engine.
//
// Example: "^ab.c\..{3}$" must start with "ab", so only names containing "ab" are worth testing.
//
// A pattern with no extractable literal (".*", "^.{5}$") has no prefilter and is reported as such, so
// the caller can tell the user the search cannot be narrowed instead of silently scanning everything.
//
// Correctness rule: only a literal that EVERY match must contain may be returned. Any run that sits
// inside an alternation branch is not required (the other branch can match without it), so this walker
// takes the longest run from the branch structure's COMMON path and ignores branch-only text. Missing an
// extractable literal only costs speed; returning a wrong one drops real results.
internal static class RegexLiteralExtractor
{
    // The longest run of literal characters any match must contain. Empty when the pattern has none.
    public static string ExtractRequiredLiteral(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return string.Empty;

        if (HasTopLevelAlternation(pattern))
            return string.Empty;

        var best = string.Empty;
        var current = new System.Text.StringBuilder();
        var anchoredAtStart = false;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '\\':
                    // An escaped metacharacter is a literal character. An escaped class (\d, \w) is not.
                    if (i + 1 < pattern.Length)
                    {
                        var next = pattern[i + 1];
                        if (IsEscapedClass(next))
                            Flush(current, ref best);
                        else
                            current.Append(next);
                        i++;
                    }
                    continue;

                case '^':
                    anchoredAtStart = current.Length == 0;
                    if (current.Length > 0)
                        Flush(current, ref best);
                    continue;

                case '$':
                    // An end anchor adds no literal but does not break the run before it.
                    continue;

                case '.':
                case '*':
                case '+':
                case '?':
                case '|':
                case '(':
                case ')':
                    Flush(current, ref best);
                    anchoredAtStart = false;
                    if (c == '(')
                        i = SkipGroupPrefix(pattern, i);
                    continue;

                case '{':
                    // A quantifier body ("{3}", "{2,5}") is a repeat count, not literal text.
                    Flush(current, ref best);
                    anchoredAtStart = false;
                    i = SkipUntil(pattern, i, '}');
                    continue;

                case '}':
                    continue;

                case '[':
                    Flush(current, ref best);
                    anchoredAtStart = false;
                    i = SkipCharacterClass(pattern, i);
                    continue;

                case ']':
                    continue;
            }

            current.Append(c);
        }

        Flush(current, ref best);
        return best;
    }

    // Any '|' that is not inside a character class means the pattern chooses between alternatives, and
    // text from one branch cannot be required of the others. Bailing out entirely is the safe reading.
    private static bool HasTopLevelAlternation(string pattern)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\')
            {
                i++;
                continue;
            }

            if (c == '[')
            {
                i = SkipCharacterClass(pattern, i);
                continue;
            }

            if (c == '|')
                return true;
        }

        return false;
    }

    private static void Flush(System.Text.StringBuilder current, ref string best)
    {
        if (current.Length > best.Length)
            best = current.ToString();

        current.Clear();
    }

    // \d \w \s and friends match a CLASS of characters, so they contribute no literal text.
    private static bool IsEscapedClass(char c) => char.IsLetter(c) || c == 'b' || c == 'B';

    // Advances past a "[...]" class, honouring a leading ']' and the "[:name:]" form so the class's own
    // brackets are not mistaken for a literal.
    private static int SkipCharacterClass(string pattern, int openIndex)
    {
        var i = openIndex + 1;
        if (i < pattern.Length && pattern[i] == '^')
            i++;
        if (i < pattern.Length && pattern[i] == ']')
            i++;

        while (i < pattern.Length && pattern[i] != ']')
        {
            if (pattern[i] == '\\')
                i++;
            i++;
        }

        return i;
    }

    private static int SkipUntil(string pattern, int from, char terminator)
    {
        for (var i = from; i < pattern.Length; i++)
        {
            if (pattern[i] == terminator)
                return i;
        }

        return pattern.Length;
    }

    // Steps past a group's opening syntax: "(?:" "(?<name>" "(?=" "(?!" and the bare "(". Anything the
    // group itself contains is scanned normally.
    private static int SkipGroupPrefix(string pattern, int openIndex)
    {
        var i = openIndex + 1;
        if (i < pattern.Length && pattern[i] == '?')
        {
            i++;
            if (i < pattern.Length && (pattern[i] == '<' || pattern[i] == '\''))
            {
                var closer = pattern[i] == '<' ? '>' : '\'';
                i = SkipUntil(pattern, i, closer);
            }
        }

        return i;
    }

    // True when the extracted literal is required to sit at the very start of the text, which lets a
    // caller answer "does this name even start with it" instead of a general substring scan.
    public static bool IsPrefixAnchored(string pattern)
        => pattern.StartsWith('^');
}
