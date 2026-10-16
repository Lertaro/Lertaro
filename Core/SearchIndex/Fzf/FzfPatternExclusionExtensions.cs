namespace Lertaro.Core.SearchIndex.Fzf;

// The exclusion (`!term`) rule for FzfPattern, split out (composition, as extension methods -- not a
// partial class) purely to keep FzfPattern.cs under the repo's per-file line limit; these helpers hold
// no state of their own and only read what FzfPattern already publishes.
internal static class FzfPatternExclusionExtensions
{
    // `!term` drops a candidate whose OWN NAME contains term, and an excluded term is deliberately never
    // pinyin-expanded (see Site/user-guide/search-syntax.md) -- so the name is the only text it can ever
    // be compared against. Every matcher here tries the name first and then re-runs the WHOLE pattern
    // against each baked alias, and a transliteration can't contain the excluded text: FzfPattern's own
    // TryMatchSet then lets that term's set pass vacuously, and the candidate the name match had just
    // rejected comes back one tier later. That is how `fhcq !王菲` still listed 王菲凤凰传奇.txt.
    //
    // So callers must veto on the name BEFORE any alias, mixed-alphabet or ancestor tier runs. This is
    // the same check TryMatch already applies to the name -- TryMatch's verdict cannot be reused, since
    // the alias tiers exist precisely for names that failed it.
    //
    // True when `name` violates none of the query's exclusions. Only worth calling when
    // pattern.HasExclusions, so a query without `!` pays nothing.
    public static bool ExclusionsPass(this FzfPattern pattern, ReadOnlySpan<char> name)
    {
        foreach (var set in pattern.EffectiveSets)
        {
            foreach (var term in set.Terms)
            {
                if (!term.Inverse)
                    continue;
                if (FzfAlgorithm.Match(term.Kind, name, term.Text, term.CaseSensitive, FzfScoringScheme.Default).IsMatch)
                    return false;
            }
        }

        return true;
    }
}
