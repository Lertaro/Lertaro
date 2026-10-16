namespace Lertaro.Core.SearchIndex.Fzf;

// How a query term is read against a TRANSLITERATION -- an alias provider's pinyin, or whatever a
// non-pinyin provider emits -- as opposed to against the candidate's own text. Kept apart from
// FzfPattern.cs (composition, not a partial class) for the same reason FzfPatternExclusionExtensions and
// FzfPatternAliasMatchExtensions are: that file's per-file line budget.
//
// A transliteration is a SPELLING of the candidate, not the text the user typed, and the two readings a
// term can take there are not interchangeable:
//
//   * contiguous -- "jt" inside 出版集团's initials alias "cbcbjt". Tight, and the only reading an
//     operator (`^`, `$`, `=`, `'x'`) can mean.
//   * subsequence -- "gxiao", 高's INITIAL followed by 效's WHOLE reading. No alias of that name can ever
//     contain it as an unbroken run, so an abbreviation that straddles syllables needs the loose reading;
//     taking it away with the fuzzy setting is what left such a query unable to reach its own file at all
//     (measured: 0.3% of these reached their name in exact mode against 76% in fuzzy mode).
//
// So the alias tiers read every positive Exact/Fuzzy term BOTH ways, CONTIGUOUS FIRST: a term that is a
// real run is still explained by that run (with its own score, span and tier), and the loose reading only
// decides terms no run can. The order is load-bearing, not cosmetic -- the other way round, the loose
// reading of "jt" matches wide across a 188-character alias, the quality gate rejects that match, and the
// tight "jt" run is never examined at all.
//
// What deliberately does NOT get a second reading: an exclusion (`!term` is only ever compared against the
// candidate's own name), an operator kind (it says exactly what it means), and a provider's query form (it
// is already a rewrite of the typed term, whose own second reading supplies the loose one).
//
// The syllable-alignment rule applies to this view like any other (FzfPattern.RequiresAlignedAliases is
// unconditional now): a match may not BEGIN inside one of the candidate's readings, in either mode. The
// readings above are about how much of a spelling a term may consume, not about where it may start.
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

    internal static FzfPattern Build(FzfPattern source)
        => new(
            source.TargetDrive,
            Twin(source.TermSets),
            source.OrGroups == null ? null : TwinGroups(source.OrGroups));

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
