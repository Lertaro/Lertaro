using System.Runtime.CompilerServices;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.Core.Tests.SearchRanking;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.Tests.SearchIndex;

// The alias lane reads an Exact term a second, loose way (see FzfPatternAliasReading) so an
// abbreviation that straddles syllables still reaches its file in exact mode. That loose reading is
// gated by the same tightness the painter applies; without the gate it also accepted a scatter the
// painter refuses, and since the literal read used to be skipped whenever a live provider existed, such
// a row could rank on a hit the user cannot see ("zbl" against a name whose only "z b l" are the
// letters of "z-library" and "1lib").
[TestClass]
[DoNotParallelize]
public sealed class AliasLooseReadingTests
{
    private const string Text =
        "商务印书馆汉译世界学术名著丛书28册套装 (约翰.罗 等) (z-library.sk, 1lib.sk, z-lib.sk).epub";

    [TestMethod]
    public void ExactMode_DoesNotReachANameByAliasScatter()
    {
        using var scope = new IdentityAliasScope();
        var before = SearchContext.DefaultFuzzyMatchEnabled;
        try
        {
            SearchContext.DefaultFuzzyMatchEnabled = false;
            Assert.IsFalse(FuzzyMatcher.IsMatch("zbl", Text));

            // The loose reading still admits a tight abbreviation inside the same alias.
            Assert.IsTrue(FuzzyMatcher.IsMatch("zL", Text));
        }
        finally
        {
            SearchContext.DefaultFuzzyMatchEnabled = before;
        }
    }

    [TestMethod]
    public void FuzzyMode_ReachesTheNameThroughTheLiteralRead()
    {
        using var scope = new IdentityAliasScope();
        var before = SearchContext.DefaultFuzzyMatchEnabled;
        try
        {
            SearchContext.DefaultFuzzyMatchEnabled = true;
            Assert.IsTrue(FuzzyMatcher.IsMatch("zbl", Text));
        }
        finally
        {
            SearchContext.DefaultFuzzyMatchEnabled = before;
        }
    }

    // #440's absence claim is about candidate generation, not ranking: an exact `zbl` must never
    // generate the loose alias row. The fixture carries both sides, so this pins the shared gate rather
    // than asserting absence from a list that was already filtered by ranking.
    [TestMethod]
    public void Reported440_CandidateGate_RejectsLooseAliasAndKeepsTightAlias()
    {
        var reported = RankingCaseFixtureLoader
            .LoadReportedCases(FixturePath(["..", "SearchRanking", "Fixtures", "reported-search-cases.v1.json"]))
            .Single(item => item.Id == "440-zbl-alias-scatter");
        var loose = reported.Corpus.Single(row => row.Token == "loose-alias-negative");
        var tight = reported.Corpus.Single(row => row.Token == "tight-alias-positive");
        Assert.IsFalse(loose.ExpectedAcceptanceBySharedTightnessGate ?? true);
        Assert.IsTrue(tight.ExpectedAcceptanceBySharedTightnessGate ?? false);

        var looseOccurrence = AliasMatchRules.TightestOccurrence(loose.DisplayName, "zbl", caseSensitive: false);
        var tightOccurrence = AliasMatchRules.TightestOccurrence(tight.DisplayName, "zbl", caseSensitive: false);
        Assert.IsNotNull(looseOccurrence);
        Assert.IsNotNull(tightOccurrence);
        Assert.AreEqual(15, looseOccurrence[^1] - looseOccurrence[0] + 1);
        Assert.AreEqual(11, tightOccurrence[^1] - tightOccurrence[0] + 1);
        Assert.IsFalse(AliasMatchRules.IsTightOccurrence(3, 15));
        Assert.IsTrue(AliasMatchRules.IsTightOccurrence(3, 11));

        // The shared gate itself is deterministic; FuzzyMatcher additionally depends on whichever alias
        // providers other test classes have registered/filtered, so pin the gate rather than that global.
        SearchContext.FuzzyMatchEnabled = false;
        try
        {
            var pattern = Lertaro.Core.SearchIndex.Fzf.FzfPattern.Parse("zbl");
            var reading = pattern.ReadingFor('\'');
            Assert.IsTrue(reading.TryMatch(tight.DisplayName, out var tightMatch, Lertaro.Core.SearchIndex.Fzf.FzfScoringScheme.Default));
            Assert.IsTrue(
                pattern.IsAcceptableAliasMatch(tightMatch),
                "the 11-character tight occurrence must pass the shared candidate gate");
            Assert.IsTrue(reading.TryMatch(loose.DisplayName, out var looseMatch, Lertaro.Core.SearchIndex.Fzf.FzfScoringScheme.Default));
            Assert.IsFalse(
                pattern.IsAcceptableAliasMatch(looseMatch),
                "the 15-character scatter must not pass the shared candidate gate");
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = true;
        }
    }

    private static string FixturePath(string[] relative, [CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, Path.Combine(relative)));

    // Registers an alias provider whose only alias is the text itself -- the shape the pinyin provider
    // produces for an already-Simplified name. The provider stays registered (there is no unregister) but
    // is filtered out for every later test on dispose.
    internal sealed class IdentityAliasScope : IDisposable
    {
        private readonly Func<IAliasProvider, bool> _previous;

        public IdentityAliasScope()
        {
            _previous = AliasProviderRegistry.FilterFunc;
            AliasProviderRegistry.Register(new IdentityAliasProvider());
        }

        public void Dispose() => AliasProviderRegistry.FilterFunc = p => p is not IdentityAliasProvider && _previous(p);
    }

    private sealed class IdentityAliasProvider : IAliasProvider
    {
        public IReadOnlyList<(char Start, char End)> InputRanges { get; } = new[] { ('\u4e00', '\u9fff') };
        public IReadOnlyList<(char Start, char End)> OutputRanges { get; } = new[] { ('a', 'z') };
        public char SyllableSeparator => '\'';
        public bool CanHandle(string text) => AliasProviderRegistry.HasNonAscii(text);
        public IEnumerable<string> GetAliases(string text) => [text];
    }
}
