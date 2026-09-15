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
// Correctness rule: only a literal that EVERY match must contain may be returned. Missing an extractable
// literal only costs speed; returning a wrong one drops real results, so every rule below is stated in the
// conservative direction.
//
// Four traps this walker exists inside, all of which produced a WRONG literal before being fixed (they were
// invisible while nothing consumed the result, and became wrong results the moment it fed the prefilter):
//
//   1. An optional atom is not required. "ab?c" matches "ac", so "b" cannot be claimed -- the same for "*"
//      and for "{0,...}".
//   2. The run must stay CONTIGUOUS. After dropping an optional character the two sides cannot be joined:
//      "ab?c" leaves "a" and "c" required, but "ac" is no substring of any match.
//   3. Text either side of a GROUP cannot be joined, because the group sits between them -- and each side
//      is ended rather than one continuing through the other. "abc(def)" requires "abc" and "def", never
//      "abcdef".
//   4. A group that chooses between alternatives contributes nothing at all, so it is skipped whole; text
//      OUTSIDE it is still required, which is why "\.(?:ogg|mp3)$" yields "." rather than nothing.
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

        // Whether the character just appended could still be removed from every match by a quantifier that
        // the next token brings. See the '?'/'*'/'{' cases: a run is only "required" up to the first
        // character that can be quantified away.
        var lastIsOptional = false;

        // False once a character has been dropped from the run because it was optional. The run is a
        // CONTIGUOUS substring requirement, so the character after a dropped one can no longer be joined to
        // what came before it: for "ab?c" the remaining required characters are "a" and "c", but "ac" is not
        // a substring of any match ("ac" does not contain it), and returning that would be a wrong literal
        // rather than merely a weak one. The run is closed at that point and a new one starts.
        var runIsContiguous = true;

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
                        {
                            Flush(current, ref best);
                            lastIsOptional = false;
                            runIsContiguous = true;
                        }
                        else
                        {
                            current.Append(next);
                            lastIsOptional = true;
                        }
                        i++;
                    }
                    continue;

                case '^':
                    anchoredAtStart = current.Length == 0;
                    if (current.Length > 0)
                        Flush(current, ref best);
                    lastIsOptional = false;
                    runIsContiguous = true;
                    continue;

                case '$':
                    // An end anchor adds no literal but does not break the run before it.
                    continue;

                // An optional quantifier makes the atom BEFORE it optional, so that character must not be
                // claimed as required: "ab?c" matches "ac", and a prefilter demanding "ab" would drop it.
                // '*' is the same at zero repetitions. ('+' keeps its atom required, so it is not here.)
                case '?':
                case '*':
                    if (lastIsOptional && current.Length > 0)
                    {
                        current.Length--;
                        // The dropped character was the join between the run so far and whatever follows.
                        runIsContiguous = false;
                    }
                    lastIsOptional = false;
                    continue;

                case '.':
                case '+':
                case '|':
                case '(':
                    Flush(current, ref best);
                    anchoredAtStart = false;
                    lastIsOptional = false;
                    runIsContiguous = true;
                    if (c == '(')
                    {
                        // A group breaks the run at BOTH ends -- text before it and text after it are not
                        // adjacent in a match, so they can never be one substring. That is also why the
                        // ')' case below does not resume the run: giving up the pair of short runs costs a
                        // little prefilter strength and removes every question of whether a group is
                        // optional or quantified, which is the trade this file's own rule asks for.
                        var close = SkipAlternationGroup(pattern, i);
                        if (HasAlternationAtOwnLevel(pattern, i, close))
                        {
                            // Branch contents are not required of every match, so they are skipped whole.
                            i = close;
                            runIsContiguous = false;
                        }
                        else
                        {
                            // A plain group's contents ARE required, so they are scanned normally; the run
                            // simply starts fresh inside it.
                            i = SkipGroupPrefix(pattern, i);
                        }
                    }
                    continue;

                // A group's closing parenthesis adds no literal text of its own and, per the '(' case above,
                // closes the side it opened: the run before the group is not adjacent to the run after it,
                // so both are ended rather than one being continued through the other.
                case ')':
                    Flush(current, ref best);
                    runIsContiguous = true;
                    lastIsOptional = false;
                    continue;

                case '{':
                    // A quantifier body ("{3}", "{2,5}") is a repeat count, not literal text; a body whose
                    // minimum is zero ("{0,3}", "{0}") also makes the preceding character optional.
                    if (StartsAtZero(pattern, i))
                    {
                        if (lastIsOptional && current.Length > 0)
                        {
                            current.Length--;
                            runIsContiguous = false;
                        }
                    }

                    Flush(current, ref best);
                    anchoredAtStart = false;
                    lastIsOptional = false;
                    runIsContiguous = true;
                    i = SkipUntil(pattern, i, '}');
                    continue;

                case '}':
                    continue;

                case '[':
                    Flush(current, ref best);
                    anchoredAtStart = false;
                    lastIsOptional = false;
                    runIsContiguous = true;
                    i = SkipCharacterClass(pattern, i);
                    continue;

                case ']':
                    continue;
            }

            // A character that follows a dropped one starts a new run rather than extending the old.
            if (!runIsContiguous && current.Length > 0)
                Flush(current, ref best);

            runIsContiguous = true;
            current.Append(c);
            lastIsOptional = true;
        }

        Flush(current, ref best);
        return best;
    }

    // True when the pattern chooses between alternatives at its TOP level, where no text at all can be
    // required of every match ("readme|notes" matches either word). An alternation nested inside a group is
    // a different matter: "\.(?:ogg|mp3)$" still requires the "\." before it and the "$" after it, and
    // bailing out for those was throwing away the most common real-world pattern there is (a list of
    // extensions). Those groups are skipped as a unit instead -- see SkipAlternationGroup.
    private static bool HasTopLevelAlternation(string pattern)
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

            // Only depth 0 is unresolvable. Deeper pipes are a group's business, and the group as a whole
            // is still required text as far as the run outside it is concerned.
            if (c == '|' && depth == 0)
                return true;
        }

        return false;
    }

    // Advances past a group whose contents choose between alternatives, returning the index of its closing
    // ')' (or the end of the pattern when it is unterminated). Nothing inside is claimed as literal: which
    // branch matched decides which characters are present.
    private static int SkipAlternationGroup(string pattern, int openIndex)
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

    // True when the group spanning `openIndex`..`closeIndex` contains a '|' at its own nesting level, i.e.
    // the group itself is a set of alternatives, so no character inside it is required of every match.
    private static bool HasAlternationAtOwnLevel(string pattern, int openIndex, int closeIndex)
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

    // True when a "{...}" quantifier body at `openIndex` allows zero repetitions ("{0}", "{0,3}"), which
    // makes the atom before it optional. An unparseable body counts as not-zero, matching the "miss an
    // extractable literal rather than return a wrong one" rule only in the safe direction: a repeat with a
    // minimum above zero keeps its atom required whatever the upper bound is.
    private static bool StartsAtZero(string pattern, int openIndex)
    {
        var i = openIndex + 1;
        if (i >= pattern.Length)
            return false;

        // Optional whitespace is not legal in a quantifier, but skipping it costs nothing.
        while (i < pattern.Length && pattern[i] == ' ')
            i++;

        if (i < pattern.Length && pattern[i] == '0')
        {
            var after = i + 1;
            return after >= pattern.Length || pattern[after] == ',' || pattern[after] == '}';
        }

        return false;
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

    // Steps past a group's opening syntax -- "(?:" "(?<name>" "(?=" "(?!" and the bare "(" -- and returns
    // the index of the LAST character consumed, so the caller's own loop increment lands on the group's
    // first content character. (Returning that content index instead silently skipped a character of every
    // group: the caller adds one on top of whatever this returns.)
    //
    // ")" is deliberately NOT a break in the scan: the group's contents are required text, so a run has to
    // pass straight through it. Treating ")" as a boundary is what made "abc(def)" report "abc".
    private static int SkipGroupPrefix(string pattern, int openIndex)
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

    // True when the extracted literal is required to sit at the very start of the text, which lets a
    // caller answer "does this name even start with it" instead of a general substring scan.
    public static bool IsPrefixAnchored(string pattern)
        => pattern.StartsWith('^');
}
