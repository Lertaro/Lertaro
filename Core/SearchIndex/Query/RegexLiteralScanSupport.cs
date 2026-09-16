namespace Lertaro.Core.SearchIndex.Query;

// Parsing helpers extracted from RegexLiteralExtractor to keep the extractor's state machine under the
// repository's per-file line limit. This class has no state of its own; it only scans regex syntax.
internal static class RegexLiteralScanSupport
{
    internal static bool HasTopLevelAlternation(string pattern)
    {
        var depth = 0;
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

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                if (depth > 0)
                    depth--;
                continue;
            }

            if (c == '|' && depth == 0)
                return true;
        }

        return false;
    }

    // A quantifier immediately after a group applies to the whole group. Treating only the last character
    // as optional is unsound for '(ab)?c' and '(ab){0,2}c': both can match 'c', so neither 'a' nor 'b' is
    // required. An unparseable quantifier is left conservative: it does not make the group optional.
    internal static bool IsOptionalGroup(string pattern, int closeIndex)
    {
        var next = closeIndex + 1;
        if (next >= pattern.Length)
            return false;

        if (pattern[next] is '?' or '*')
            return true;
        if (pattern[next] != '{')
            return false;

        return StartsAtZero(pattern, next);
    }

    internal static int SkipAlternationGroup(string pattern, int openIndex)
    {
        var depth = 1;
        for (var i = openIndex + 1; i < pattern.Length; i++)
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

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return pattern.Length - 1;
    }

    internal static bool HasAlternationAtOwnLevel(string pattern, int openIndex, int closeIndex)
    {
        var depth = 1;
        for (var i = openIndex + 1; i < closeIndex; i++)
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

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                depth--;
                continue;
            }

            if (c == '|' && depth == 1)
                return true;
        }

        return false;
    }

    internal static void Flush(System.Text.StringBuilder current, ref string best)
    {
        if (current.Length > best.Length)
            best = current.ToString();

        current.Clear();
    }

    // An escape that is NOT the literal character it looks like: a class or anchor (\d \w \p{L} \b), or a
    // backreference (\1). Both must end the run rather than contribute text -- "\1" stands for the group it
    // points at and never for "1", so claiming it would demand a character no match of "^(ab)\1$" contains.
    // Everything else is an escaped metacharacter and IS its own literal ("\.", "\\").
    internal static bool IsEscapedNonLiteral(char c) => char.IsLetter(c) || char.IsDigit(c);

    internal static int SkipCharacterClass(string pattern, int openIndex)
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

    internal static bool StartsAtZero(string pattern, int openIndex)
    {
        var i = openIndex + 1;
        if (i >= pattern.Length)
            return false;

        while (i < pattern.Length && pattern[i] == ' ')
            i++;

        if (i < pattern.Length && pattern[i] == '0')
        {
            var after = i + 1;
            return after >= pattern.Length || pattern[after] == ',' || pattern[after] == '}';
        }

        return false;
    }

    internal static int SkipUntil(string pattern, int from, char terminator)
    {
        for (var i = from; i < pattern.Length; i++)
        {
            if (pattern[i] == terminator)
                return i;
        }

        return pattern.Length;
    }

    internal static int SkipGroupPrefix(string pattern, int openIndex)
    {
        var i = openIndex + 1;
        if (i >= pattern.Length || pattern[i] != '?')
            return openIndex;

        i++;
        if (i < pattern.Length && (pattern[i] == '<' || pattern[i] == '\''))
        {
            var closer = pattern[i] == '<' ? '>' : '\'';
            return SkipUntil(pattern, i, closer);
        }

        return i;
    }

    // True for a "(?" group that carries none of the match's own text: a lookahead ("(?=" / "(?!"), a
    // lookbehind ("(?<=" / "(?<!"), an inline option setting ("(?i)", "(?i:...") or a comment ("(?#...").
    //
    // Only the ":" (non-capturing) and "(?<name>" / "(?'name'" (capturing) forms are ordinary groups that
    // DO contain the match's text; everything else opening with "(?" is metadata about how to match rather
    // than something a candidate must contain. The negative forms are the dangerous ones -- they assert
    // the text is ABSENT, so scanning their contents as required inverts the assertion: see
    // RegexLiteralExtractor's '(' case.
    internal static bool IsTextlessGroup(string pattern, int openIndex)
    {
        var i = openIndex + 1;
        if (i + 1 >= pattern.Length || pattern[i] != '?')
            return false;

        var kind = pattern[i + 1];
        if (kind == ':')
            return false;

        if (kind is not ('<' or '\''))
            return true;

        // "(?<" opens either a lookbehind or a named group; "(?'" only ever names a group. The marker
        // right after the bracket decides, which is what keeps "(?<=pre)" out of the named-group branch
        // that would otherwise swallow the rest of the pattern looking for a '>'.
        var after = i + 2;
        return after < pattern.Length && (pattern[after] == '=' || pattern[after] == '!');
    }
}
