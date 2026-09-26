using static Lertaro.Core.SearchIndex.Query.RegexLiteralScanSupport;

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
// Five traps this walker exists inside, all of which produced a WRONG literal before being fixed (they were
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
//   5. A group that is not the match's own text contributes nothing either -- see the '(' case. A NEGATIVE
//      assertion is the wrong-literal case here, not just the weak-literal one: "/^(?!.*tmp).*\.md$/" says
//      "tmp" must be ABSENT, and demanding it returned nothing for "readme.md".
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
                    // An escaped metacharacter is a literal character. A class (\d, \w), an anchor (\b) or
                    // a backreference (\1) is not, and ends the run instead.
                    if (i + 1 < pattern.Length)
                    {
                        var next = pattern[i + 1];
                        if (IsEscapedNonLiteral(next))
                        {
                            Flush(current, ref best);
                            lastIsOptional = false;
                            runIsContiguous = true;
                        }
                        else
                        {
                            // The escaped literal is a separate atom, so it cannot be joined onto a run
                            // that an optional atom already broke: for "ab?\.c" the required runs are "a"
                            // and ".c", never "a." -- the same contiguity rule the '?' case below states.
                            // Without this the returned value was a string no match has to contain
                            // ("readm." for "^readme?\.md$"), which breaks the substring contract this
                            // file's header states, even though the current consumer (a character mask,
                            // which only asks whether each character occurs) does not notice.
                            if (!runIsContiguous && current.Length > 0)
                                Flush(current, ref best);
                            runIsContiguous = true;
                            current.Append(next);
                            lastIsOptional = true;
                        }
                        i++;
                    }
                    continue;

                case '^':
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
                    lastIsOptional = false;
                    runIsContiguous = true;
                    if (c == '(')
                    {
                        // A group that carries none of the match's own text -- a lookaround, an inline
                        // option setting, a comment -- contributes nothing, and must be skipped whole.
                        // Scanning a NEGATIVE assertion's contents is not merely weak, it is wrong: the
                        // assertion says that text must be ABSENT, so demanding it of every candidate
                        // drops exactly the names the pattern exists to match. "/^(?!.*tmp).*\.md$/" used
                        // to yield "tmp" and so returned nothing for "readme.md". Positive assertions do
                        // require their contents, but skipping those too costs only prefilter strength
                        // and keeps every group of this shape on one rule.
                        var close = SkipAlternationGroup(pattern, i);
                        if (IsTextlessGroup(pattern, i))
                        {
                            i = close;
                            runIsContiguous = false;
                            continue;
                        }

                        // A group breaks the run at BOTH ends -- text before it and text after it are not
                        // adjacent in a match, so they can never be one substring. That is also why the
                        // ')' case below does not resume the run: giving up the pair of short runs costs a
                        // little prefilter strength and removes every question of whether a group is
                        // optional or quantified, which is the trade this file's own rule asks for.
                        // Find the close before deciding whether the group itself is required: a quantifier
                        // after the close applies to the whole group, not merely to the last character that
                        // happened to be scanned inside it.
                        var hasAlternation = HasAlternationAtOwnLevel(pattern, i, close);
                        if (hasAlternation || IsOptionalGroup(pattern, close))
                        {
                            // Branch contents are not required of every match. The same is true for a plain
                            // group followed by '?', '*', or a zero-minimum repeat: '(ab)?c' matches 'c', so
                            // 'ab' must never reach the required-literal mask.
                            i = close;
                            runIsContiguous = false;
                        }
                        else
                        {
                            // A required plain group's contents ARE required, so they are scanned normally;
                            // the run simply starts fresh inside it.
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
                    lastIsOptional = false;
                    runIsContiguous = true;
                    i = SkipUntil(pattern, i, '}');
                    continue;

                case '}':
                    continue;

                case '[':
                    Flush(current, ref best);
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

}
