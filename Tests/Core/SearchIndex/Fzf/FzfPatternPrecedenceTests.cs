using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

[TestClass]
[DoNotParallelize]
public sealed class FzfPatternPrecedenceTests
{
    [TestMethod]
    public void Parse_AndFirstPreservesAliasAlternativesInsideEachAndGroup()
    {
        var pattern = new FzfPattern(null, Array.Empty<FzfTermSet>(), new[]
        {
            new FzfTermGroup(new[]
            {
                new FzfTermSet(new[]
                {
                    new FzfTerm(FzfTermKind.Exact, false, "报", false),
                    new FzfTerm(FzfTermKind.Exact, false, "bao", false, AliasForm: true),
                }),
                new FzfTermSet(new[] { new FzfTerm(FzfTermKind.Exact, false, "2024", false) }),
            }),
        });

        Assert.IsTrue(pattern.TryMatch("bao-2024.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("bao.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void Parse_AndFirstBuildsGroupsWithIndependentTermSets()
    {
        var pattern = FzfPattern.Parse("report | summary 2024");

        Assert.IsNotNull(pattern.OrGroups);
        Assert.HasCount(2, pattern.OrGroups);
        Assert.HasCount(1, pattern.OrGroups[0].Sets);
        Assert.HasCount(1, pattern.OrGroups[0].Sets[0].Terms);
        Assert.HasCount(2, pattern.OrGroups[1].Sets);
        Assert.AreEqual("report", pattern.OrGroups[0].Sets[0].Terms[0].Text);
        Assert.AreEqual("summary", pattern.OrGroups[1].Sets[0].Terms[0].Text);
        Assert.AreEqual("2024", pattern.OrGroups[1].Sets[1].Terms[0].Text);
    }

    [TestMethod]
    public void TryMatch_AndFirstMatchesEitherTheLoneTermOrTheConjunction()
    {
        var pattern = FzfPattern.Parse("report | summary 2024");

        Assert.IsTrue(pattern.TryMatch("report.txt", out _, FzfScoringScheme.Default));
        Assert.IsTrue(pattern.TryMatch("summary-2024.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("summary.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("2024.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_OrFirstKeepsTheHistoricalReading()
    {
        var previous = SearchContext.AndFirstPrecedence;
        SearchContext.AndFirstPrecedence = false;
        try
        {
            var pattern = FzfPattern.Parse("report | summary 2024");

            Assert.IsNull(pattern.OrGroups);
            Assert.HasCount(2, pattern.TermSets);
            Assert.IsTrue(pattern.TryMatch("summary-2024.txt", out _, FzfScoringScheme.Default));
            Assert.IsTrue(pattern.TryMatch("report-2024.txt", out _, FzfScoringScheme.Default));
            Assert.IsFalse(pattern.TryMatch("summary.txt", out _, FzfScoringScheme.Default));
        }
        finally
        {
            SearchContext.AndFirstPrecedence = previous;
        }
    }

    [TestMethod]
    public void Parse_AndFirstKeepsFlatShapeWhenOperatorsDoNotMix()
    {
        Assert.IsNull(FzfPattern.Parse("read me").OrGroups);
        Assert.IsNull(FzfPattern.Parse("readme | rdm").OrGroups);
    }

    [TestMethod]
    public void GetTotalTermLength_AndFirstTakesTheLongestAlternativeGroup()
    {
        var pattern = FzfPattern.Parse("read | summary 2024");

        Assert.AreEqual("summary".Length + "2024".Length, pattern.GetTotalTermLength());
    }

    [TestMethod]
    public void Parse_ProcessDefaultAndFirstAppliesWithoutPerRequestValue()
    {
        var previous = SearchContext.DefaultAndFirstPrecedence;
        SearchContext.DefaultAndFirstPrecedence = true;
        try { Assert.IsNotNull(FzfPattern.Parse("read | me 2").OrGroups); }
        finally { SearchContext.DefaultAndFirstPrecedence = previous; }
    }

    [TestMethod]
    public void Parse_PerRequestPrecedenceOverridesProcessDefault()
    {
        var previousDefault = SearchContext.DefaultAndFirstPrecedence;
        var previousRequest = SearchContext.AndFirstPrecedence;
        SearchContext.DefaultAndFirstPrecedence = true;
        SearchContext.AndFirstPrecedence = false;
        try { Assert.IsNull(FzfPattern.Parse("read | me 2").OrGroups); }
        finally
        {
            SearchContext.AndFirstPrecedence = previousRequest;
            SearchContext.DefaultAndFirstPrecedence = previousDefault;
        }
    }
}
