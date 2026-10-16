using Lertaro.Core.IndexV2.Alias;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.Tests.IndexV2.Alias;

// The joint walk exists for one thing: a query whose sets need DIFFERENT readings of the same character.
// The per-set merge cannot answer those (whichever set is merged last decides the shared spelling), so these
// tests are about the spelling that comes out, not just about a yes/no.
[TestClass]
public sealed class AliasGridJointMatcherTests
{
    private static AliasGridFormat.Index Build(string[][] sets)
    {
        var sink = new AliasByteSink();
        var text = new string('x', sets.Length);
        Assert.IsTrue(AliasGridFormat.Encode(text, new FakeGridProvider(sets), sink) > 0);

        var index = new AliasGridFormat.Index();
        Assert.IsTrue(index.TryBind(sink.Segment(0)));
        return index;
    }

    private static FzfTermSet[] Sets(params string[] terms) =>
        [.. terms.Select(term => new FzfTermSet([new FzfTerm(FzfTermKind.Exact, false, term, false)]))];

    private static string Materialize(AliasGridFormat.Index grid, AliasGridTier tier, byte[] choice)
    {
        var measure = AliasGridMatcher.Measure(grid, tier, choice);
        var buffer = new char[measure];
        var length = AliasGridMatcher.Materialize(grid, tier, FakeGridProvider.Separator, choice, buffer);
        Assert.AreEqual(measure, length);
        return new string(buffer, 0, length);
    }

    // 屏 (bing / ping) 幕 截 图 标 记: "pmjt" needs 屏's SECOND reading and "bj" its FIRST. The merge lets
    // "bj" rewrite position 0 (its 'b' is cheaper at the head of the name), and the alias then holds no 'p'
    // at all -- which is the reported "pmjtmske bj" / "pmjt bj" shape.
    private static readonly string[][] PolyphoneAtTheHead =
    [
        ["bing", "ping"], ["mu"], ["jie"], ["tu"], ["biao"], ["ji"],
    ];

    [TestMethod]
    [DataRow("pmjt", "bj")]
    [DataRow("bj", "pmjt")]
    public void TwoSets_NeedingDifferentReadingsOfOneCharacter_ShareOneChoice(string first, string second)
    {
        var grid = Build(PolyphoneAtTheHead);
        var choice = new byte[grid.PositionCount];

        Assert.IsTrue(AliasGridJointMatcher.TryMatchSets(grid, Sets(first, second), FakeGridProvider.Separator, AliasGridTier.Full, choice, out var spellsAReading));
        Assert.AreEqual(1, choice[0], "屏 stands for 'pmjt's p; 'bj' takes 标's b instead");
        Assert.IsFalse(spellsAReading, "every piece here is one letter: no reading was spelled out");

        var alias = Materialize(grid, AliasGridTier.Full, choice);
        Assert.AreEqual("ping" + "mu" + "jie" + "tu" + "biao" + "ji", alias);
        Assert.IsTrue(alias.Contains('p'), "the spelling both sets were witnessed against still holds the p");
    }

    [TestMethod]
    public void OneSet_IsLeftToThePerTermWalk()
    {
        // A single set is answered exactly by AliasGridMatcher's own reachability walk; running the joint
        // one would only cost more for the same verdict.
        var grid = Build(PolyphoneAtTheHead);
        var choice = new byte[grid.PositionCount];

        Assert.IsFalse(AliasGridJointMatcher.TryMatchSets(grid, Sets("pingmu"), FakeGridProvider.Separator, AliasGridTier.Full, choice, out _));
        Assert.AreEqual(0, choice.Sum(b => (int)b), "a declined query leaves the caller's choice untouched");
    }

    [TestMethod]
    public void OperatorTerms_AreLeftToTheirOwnWalk()
    {
        var grid = Build(PolyphoneAtTheHead);
        var choice = new byte[grid.PositionCount];
        FzfTermSet[] sets = [new FzfTermSet([new FzfTerm(FzfTermKind.Prefix, false, "ping", false)]), new FzfTermSet([new FzfTerm(FzfTermKind.Exact, false, "bj", false)])];

        Assert.IsTrue(AliasGridMatcher.TryMatchTerm(grid, "ping", FzfTermKind.Prefix, AliasGridTier.Full, FakeGridProvider.Separator, choice), "sanity: the operator walk does answer it");
        Assert.IsFalse(AliasGridJointMatcher.TryMatchSets(grid, sets, FakeGridProvider.Separator, AliasGridTier.Full, choice, out _));
    }

    [TestMethod]
    public void SetsWithNothingToSatisfy_AreRefused()
    {
        var grid = Build(PolyphoneAtTheHead);
        var choice = new byte[grid.PositionCount];
        FzfTermSet[] sets = [new FzfTermSet([new FzfTerm(FzfTermKind.Exact, false, "bj", false)]), new FzfTermSet([new FzfTerm(FzfTermKind.Exact, true, "tu", false)])];

        // An excluded-only set has no letters to witness; the merge handles that shape, this walk declines.
        Assert.IsFalse(AliasGridJointMatcher.TryMatchSets(grid, sets, FakeGridProvider.Separator, AliasGridTier.Full, choice, out _));
    }

    [TestMethod]
    public void JointChoice_ReportsWhetherAReadingWasSpelledOut()
    {
        // "pj" abbreviates two characters; "aoke" gives 敖 and 克 their whole readings. The flag the alias
        // quality bar reads has to come out of the shared walk too, or a spelled-out query would be judged
        // by the bound meant for bare abbreviations.
        var grid = Build([["bing", "ping"], ["jie"], ["ao"], ["ke"]]);
        var choice = new byte[grid.PositionCount];

        Assert.IsTrue(AliasGridJointMatcher.TryMatchSets(grid, Sets("pj", "aoke"), FakeGridProvider.Separator, AliasGridTier.Full, choice, out var spelled));
        Assert.IsTrue(spelled, "one position gave up both of its letters");
        Assert.AreEqual("pingjieaoke", Materialize(grid, AliasGridTier.Full, choice));
    }

    [TestMethod]
    public void InitialsTier_SharesOneReadingForEachLetter()
    {
        // The same conflict one tier over: "pj" wants 屏 as p, "bj" wants it as b. The merge lets the later
        // set take position 0 and the other term then has no letter to stand on; the shared choice sends
        // "bj" to 标/记 instead -- which reads off the same readings as b…j, not two different characters.
        var grid = Build(PolyphoneAtTheHead);
        var choice = new byte[grid.PositionCount];

        Assert.IsTrue(AliasGridJointMatcher.TryMatchSets(grid, Sets("pj", "bj"), FakeGridProvider.Separator, AliasGridTier.Initials, choice, out var spelled));
        Assert.IsFalse(spelled, "a term of one letter per position spells nothing out");
        Assert.AreEqual("pmjtbj", Materialize(grid, AliasGridTier.Initials, choice));
    }
}
