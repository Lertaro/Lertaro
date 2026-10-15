using Lertaro.Core.IndexV2.Alias;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.Tests.IndexV2.Alias;

[TestClass]
public sealed class AliasGridMatcherTests
{
    private static AliasGridFormat.Index Build(string[][] sets, bool[]? boundaries = null)
    {
        var sink = new AliasByteSink();
        var text = new string('x', sets.Length);
        Assert.IsTrue(AliasGridFormat.Encode(text, new FakeGridProvider(sets, boundaries), sink) > 0);

        var index = new AliasGridFormat.Index();
        Assert.IsTrue(index.TryBind(sink.Segment(0)));
        return index;
    }

    private static bool TryMatch(
        AliasGridFormat.Index grid,
        string term,
        FzfTermKind kind,
        AliasGridTier tier,
        out byte[] choice,
        byte prefill = 0)
    {
        choice = new byte[grid.PositionCount];
        if (prefill != 0)
            Array.Fill(choice, prefill);
        return AliasGridMatcher.TryMatchTerm(grid, term, kind, tier, FakeGridProvider.Separator, choice);
    }

    private static string Materialize(AliasGridFormat.Index grid, AliasGridTier tier, byte[] choice)
    {
        var buffer = new char[AliasGridMatcher.Measure(grid, tier, choice)];
        var length = AliasGridMatcher.Materialize(grid, tier, FakeGridProvider.Separator, choice, buffer);
        Assert.AreEqual(buffer.Length, length);
        return new string(buffer);
    }

    [TestMethod]
    public void FuzzyInitials_TakesOneCharacterPerPosition()
    {
        var grid = Build(new[] { new[] { "fang" }, new[] { "dan" }, new[] { "bi" }, new[] { "ji" }, new[] { "fa" } });

        Assert.IsTrue(TryMatch(grid, "fdbjf", FzfTermKind.Fuzzy, AliasGridTier.Initials, out var choice));
        Assert.AreEqual("fdbjf", Materialize(grid, AliasGridTier.Initials, choice));
    }

    [TestMethod]
    public void FuzzyInitials_UnconstrainedPositionsKeepTheCallersPrefill()
    {
        var grid = Build(new[] { new[] { "chong", "zhong" }, new[] { "guo" } });

        Assert.IsTrue(TryMatch(grid, "g", FzfTermKind.Fuzzy, AliasGridTier.Initials, out var choice, prefill: 1));

        Assert.AreEqual(1, choice[0], "position 0 was not constrained by 'g', so its prefill survives");
        Assert.AreEqual(0, choice[1], "'g' can only come from guo");
        Assert.AreEqual("zg", Materialize(grid, AliasGridTier.Initials, choice));
    }

    [TestMethod]
    public void FuzzyInitials_RejectsATermLongerThanTheName()
    {
        var grid = Build(new[] { new[] { "fang" }, new[] { "dan" }, new[] { "bi" }, new[] { "ji" }, new[] { "fa" } });

        Assert.IsFalse(TryMatch(grid, "fdubjf", FzfTermKind.Fuzzy, AliasGridTier.Initials, out _));
    }

    [TestMethod]
    public void FuzzyFull_MatchesAcrossASyllableBoundaryAndThroughAHalfTypedTail()
    {
        var grid = Build(new[] { new[] { "zhong" }, new[] { "shu" } }, new[] { false, true });

        var term = "zhong" + FakeGridProvider.Separator + "sh";
        Assert.IsTrue(TryMatch(grid, term, FzfTermKind.Fuzzy, AliasGridTier.Full, out var choice));

        Assert.AreEqual("zhong" + FakeGridProvider.Separator + "shu", Materialize(grid, AliasGridTier.Full, choice));
        Assert.AreEqual(9, AliasGridMatcher.Measure(grid, AliasGridTier.Full, choice));
    }

    [TestMethod]
    public void FuzzyFull_MatchesAScatteredTerm()
    {
        var grid = Build(new[] { new[] { "zhong" }, new[] { "shu" } }, new[] { false, true });

        Assert.IsTrue(TryMatch(grid, "zhsh", FzfTermKind.Fuzzy, AliasGridTier.Full, out _));
    }

    [TestMethod]
    public void FuzzyFull_RejectsACharacterNoReadingCanSupply()
    {
        var grid = Build(new[] { new[] { "zhong" }, new[] { "shu" } }, new[] { false, true });

        Assert.IsFalse(TryMatch(grid, "zhong" + FakeGridProvider.Separator + "x", FzfTermKind.Fuzzy, AliasGridTier.Full, out _));
    }

    [TestMethod]
    public void ExactFull_RequiresTheRunToBeContiguous()
    {
        var grid = Build(new[] { new[] { "zhong" }, new[] { "shu" } }, new[] { false, true });

        Assert.IsTrue(TryMatch(grid, "zshu", FzfTermKind.Fuzzy, AliasGridTier.Full, out _));
        Assert.IsFalse(TryMatch(grid, "zshu", FzfTermKind.Exact, AliasGridTier.Full, out _));
    }

    [TestMethod]
    public void ExactFull_PicksTheReadingTheTermNeeds()
    {
        var grid = Build(new[] { new[] { "chong", "zhong" }, new[] { "guo" } }, new[] { false, true });

        var term = "zhong" + FakeGridProvider.Separator + "guo";
        Assert.IsTrue(TryMatch(grid, term, FzfTermKind.Exact, AliasGridTier.Full, out var choice));

        Assert.AreEqual(1, choice[0], "'zhong' is the second reading of position 0");
        Assert.AreEqual(term, Materialize(grid, AliasGridTier.Full, choice));
    }

    [TestMethod]
    public void PrefixAnchor_PinsTheRunToTheAliasStart()
    {
        var grid = Build(new[] { new[] { "zhong" }, new[] { "shu" } }, new[] { false, true });

        Assert.IsTrue(TryMatch(grid, "zhong" + FakeGridProvider.Separator, FzfTermKind.Prefix, AliasGridTier.Full, out _));
        Assert.IsFalse(TryMatch(grid, "shu", FzfTermKind.Prefix, AliasGridTier.Full, out _));
        Assert.IsTrue(TryMatch(grid, "shu", FzfTermKind.Exact, AliasGridTier.Full, out _));
    }

    [TestMethod]
    public void SuffixAnchor_PinsTheRunToTheAliasEnd()
    {
        var grid = Build(new[] { new[] { "zhong" }, new[] { "shu" } }, new[] { false, true });

        Assert.IsTrue(TryMatch(grid, "shu", FzfTermKind.Suffix, AliasGridTier.Full, out _));
        Assert.IsFalse(TryMatch(grid, "zhong", FzfTermKind.Suffix, AliasGridTier.Full, out _));
    }

    [TestMethod]
    public void EqualAnchor_RequiresTheWholeAlias()
    {
        var grid = Build(new[] { new[] { "zhong" }, new[] { "shu" } }, new[] { false, true });

        var whole = "zhong" + FakeGridProvider.Separator + "shu";
        Assert.IsTrue(TryMatch(grid, whole, FzfTermKind.Equal, AliasGridTier.Full, out _));
        Assert.IsFalse(TryMatch(grid, "zhong" + FakeGridProvider.Separator, FzfTermKind.Equal, AliasGridTier.Full, out _));
    }

    [TestMethod]
    public void ExactInitials_RejectsATermThatWouldNeedOnePositionTwice()
    {
        var grid = Build(new[] { new[] { "fang", "fa" }, new[] { "dan" } });

        // 'f','a' both come from position 0, which the initials tier cannot do: that is the full tier's job.
        Assert.IsFalse(TryMatch(grid, "fa", FzfTermKind.Exact, AliasGridTier.Initials, out _));
        Assert.IsTrue(TryMatch(grid, "fa", FzfTermKind.Exact, AliasGridTier.Full, out _));
    }

    [TestMethod]
    public void Materialize_AgreesWithMeasureOnBothTiers()
    {
        var grid = Build(new[] { new[] { "zhong", "chong" }, new[] { "shu", "su" } }, new[] { false, true });

        Assert.IsTrue(TryMatch(grid, "z", FzfTermKind.Fuzzy, AliasGridTier.Initials, out var initials));
        Assert.AreEqual("zs", Materialize(grid, AliasGridTier.Initials, initials));

        Assert.IsTrue(TryMatch(grid, "zhong" + FakeGridProvider.Separator + "shu", FzfTermKind.Exact, AliasGridTier.Full, out var full));
        Assert.AreEqual("zhong" + FakeGridProvider.Separator + "shu", Materialize(grid, AliasGridTier.Full, full));
    }
}
