namespace Lertaro.Core.SearchIndex.Fzf;

// Matches a term against a transliteration, trying a contiguous run before a subsequence.
// For example, "jt" can occur as a run in an initials alias, while "gxiao" can combine 高's initial
// with 效's full reading. Plain positive Exact/Fuzzy terms allow both readings in either fuzzy mode.
//
// Contiguous matching must run first: a scattered match may fail the quality gate even when a valid
// tight run exists. Both readings must begin at a syllable boundary.
//
// Exclusions and regex clauses check the original name. Provider query forms and text-rewrite
// aliases keep their own matching rules and do not get a second reading here.
internal static class FzfPatternAliasReading
{
    /// <summary>
    /// The reading a query's aliases from a provider that declared <paramref name="separator"/> are matched
    /// with: the alias-side view when that provider has syllable structure, otherwise the query itself.
    /// </summary>
    /// <remarks>
    /// A provider that declares no separator is telling the host it cannot say where one sound ends and the
    /// next begins -- and the alignment rule is exactly that knowledge. With nothing to align to, the loose
    /// reading can no longer tell a mixed abbreviation ("gxiao" = 高's initial + 效's whole reading) from two
    /// letters spliced out of adjacent sounds ("gq" = the tail of ting + the head of qin), so such a provider
    /// keeps the strict reading it has always had. The transliterating providers this exists for -- pinyin
    /// declares a separator -- get both.
    /// </remarks>
    internal static FzfPattern ReadingFor(this FzfPattern pattern, char separator)
        => separator == '\0' ? pattern : pattern.AliasReading;

    /// <summary>
    /// The reading ONE alias of a provider is matched with: the alias-side view when that alias is a
    /// transliteration, the query itself when it is the candidate's own text rewritten.
    /// </summary>
    /// <remarks>
    /// The second reading exists for spellings -- "gxiao" is 高's initial plus 效's whole reading and is no
    /// run in any alias of that name. An alias that carries the source script instead (pinyin's Simplified
    /// spelling) says nothing about syllables, and every letter in it is already a letter of the name, so
    /// reading it loosely is the user's own precision overridden by coincidence: with fuzzy matching off,
    /// "zbl" reached 著作zsbsls.txt that way. See <see cref="AliasMatchRules.IsTransliteration(ReadOnlySpan{char})"/>.
    /// </remarks>
    internal static FzfPattern ReadingFor(this FzfPattern pattern, char separator, ReadOnlySpan<char> alias)
        => AliasMatchRules.IsTransliteration(alias) ? pattern.ReadingFor(separator) : pattern;

    // The clauses that read the candidate's own text have to survive the copy: the alias tiers answer a
    // query through this view while handing them the NAME (FzfPattern.TryMatchAlias), and a view without
    // them would silently answer a `/.../` query as if no clause had been typed.
    internal static FzfPattern Build(FzfPattern source)
        => new(
            source.TargetDrive,
            Twin(source.TermSets),
            source.OrGroups == null ? null : TwinGroups(source.OrGroups),
            source.Regexes);

    private static FzfTermSet[] Twin(FzfTermSet[] sets)
    {
        var twinned = new FzfTermSet[sets.Length];
        for (var s = 0; s < sets.Length; s++)
            twinned[s] = new FzfTermSet(Twin(sets[s].Terms));
        return twinned;
    }

    private static FzfTermGroup[] TwinGroups(FzfTermGroup[] groups)
    {
        var twinned = new FzfTermGroup[groups.Length];
        for (var g = 0; g < groups.Length; g++)
            twinned[g] = new FzfTermGroup(Twin(groups[g].Sets));
        return twinned;
    }

    private static FzfTerm[] Twin(FzfTerm[] terms)
    {
        // Every term can contribute at most one more alternative.
        var twinned = new FzfTerm[terms.Length * 2];
        var count = 0;
        foreach (var term in terms)
        {
            var other = OtherReading(term);
            if (other == null)
            {
                twinned[count++] = term;
                continue;
            }

            if (term.Kind == FzfTermKind.Fuzzy)
            {
                twinned[count++] = other.Value; // contiguous first
                twinned[count++] = term;
            }
            else
            {
                twinned[count++] = term;
                twinned[count++] = other.Value;
            }
        }

        return count == twinned.Length ? twinned : twinned[..count];
    }

    private static FzfTerm? OtherReading(FzfTerm term) => term.Inverse || term.AliasForm ? null : term.Kind switch
    {
        FzfTermKind.Exact => term with { Kind = FzfTermKind.Fuzzy },
        FzfTermKind.Fuzzy => term with { Kind = FzfTermKind.Exact },
        _ => null,
    };
}
