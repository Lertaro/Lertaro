namespace Lertaro.Core.Tests;

[TestClass]
public sealed class IndexRuleConflictTests
{
    private static IndexRuleSetting Rule(string pattern, IndexRuleEffect effect) => new() { Pattern = pattern, Effect = effect };

    [TestMethod]
    public void PathPriorityUnderExcludedPath_IsReported()
    {
        var conflicts = IndexRuleConflictDetector.Detect(new[]
        {
            Rule(@"C:\temp", IndexRuleEffect.Exclude),
            Rule(@"C:\temp\build", IndexRuleEffect.Low),
        });

        Assert.HasCount(1, conflicts);
        Assert.AreEqual(0, conflicts[0].ExcludeIndex);
        Assert.AreEqual(1, conflicts[0].PriorityIndex);
    }

    [TestMethod]
    public void ExcludePathIsAncestorOfPriorityPath_IsReported()
    {
        var conflicts = IndexRuleConflictDetector.Detect(new[]
        {
            Rule(@"C:\temp\build", IndexRuleEffect.High),
            Rule(@"C:\temp", IndexRuleEffect.Exclude),
        });

        Assert.HasCount(1, conflicts);
        Assert.AreEqual(@"C:\temp", conflicts[0].ExcludePattern);
    }

    [TestMethod]
    public void PriorityPathCoveringExcludePath_IsNotReported()
    {
        var conflicts = IndexRuleConflictDetector.Detect(new[]
        {
            Rule(@"C:\temp", IndexRuleEffect.High),
            Rule(@"C:\temp\build", IndexRuleEffect.Exclude),
        });

        Assert.HasCount(0, conflicts);
    }

    [TestMethod]
    public void BroadRegexPriorityVersusPathExclude_IsNotReported()
    {
        var conflicts = IndexRuleConflictDetector.Detect(new[]
        {
            Rule(@"C:\temp", IndexRuleEffect.Exclude),
            Rule(@"/^[^.]+$/", IndexRuleEffect.Low),
        });

        Assert.HasCount(0, conflicts);
    }

    [TestMethod]
    public void AnchoredGlobUnderExcludePath_IsReported()
    {
        var conflicts = IndexRuleConflictDetector.Detect(new[]
        {
            Rule(@"C:\temp", IndexRuleEffect.Exclude),
            Rule(@"C:\temp\**", IndexRuleEffect.Low),
        });

        Assert.HasCount(1, conflicts);
    }

    [TestMethod]
    public void AnchoredGlobAboveExcludePath_IsNotReported()
    {
        var conflicts = IndexRuleConflictDetector.Detect(new[]
        {
            Rule(@"C:\temp\build", IndexRuleEffect.Exclude),
            Rule(@"C:\temp\*", IndexRuleEffect.Low),
        });

        Assert.HasCount(0, conflicts);
    }

    [TestMethod]
    public void AnchoredGlobInSiblingDirectory_IsNotReported()
    {
        var conflicts = IndexRuleConflictDetector.Detect(new[]
        {
            Rule(@"C:\temp", IndexRuleEffect.Exclude),
            Rule(@"C:\temp2\*", IndexRuleEffect.Low),
        });

        Assert.HasCount(0, conflicts);
    }

    [TestMethod]
    public void RelativeGlobPriorityVersusPathExclude_IsNotReported()
    {
        var conflicts = IndexRuleConflictDetector.Detect(new[]
        {
            Rule(@"C:\temp", IndexRuleEffect.Exclude),
            Rule(@"**\node_modules\**", IndexRuleEffect.Low),
        });

        Assert.HasCount(0, conflicts);
    }

    [TestMethod]
    public void GlobWithWildcardInsideSegment_IsNotReported()
    {
        var conflicts = IndexRuleConflictDetector.Detect(new[]
        {
            Rule(@"C:\temp\a", IndexRuleEffect.Exclude),
            Rule(@"C:\temp\a*", IndexRuleEffect.Low),
        });

        Assert.HasCount(0, conflicts);
    }

    [TestMethod]
    public void PathPriorityMatchedByExcludeGlob_IsReported()
    {
        var conflicts = IndexRuleConflictDetector.Detect(new[]
        {
            Rule("node_modules", IndexRuleEffect.Exclude),
            Rule(@"C:\proj\node_modules\cache", IndexRuleEffect.High),
        });

        Assert.HasCount(1, conflicts);
    }

    [TestMethod]
    public void PathPriorityMatchedByExcludeRegex_IsReported()
    {
        var conflicts = IndexRuleConflictDetector.Detect(new[]
        {
            Rule(@"/^\./", IndexRuleEffect.Exclude),
            Rule(@"C:\proj\.git", IndexRuleEffect.Low),
        });

        Assert.HasCount(1, conflicts);
    }

    [TestMethod]
    public void SameTextWithDifferentEffects_IsReported()
    {
        var conflicts = IndexRuleConflictDetector.Detect(new[]
        {
            Rule("build", IndexRuleEffect.Exclude),
            Rule("build", IndexRuleEffect.High),
        });

        Assert.HasCount(1, conflicts);
    }

    [TestMethod]
    public void DisjointRules_AreNotReported()
    {
        var conflicts = IndexRuleConflictDetector.Detect(new[]
        {
            Rule(@"C:\temp", IndexRuleEffect.Exclude),
            Rule(@"D:\build", IndexRuleEffect.Low),
        });

        Assert.HasCount(0, conflicts);
    }

    [TestMethod]
    public void GlobVersusGlob_IsNotStaticallyReported()
    {
        var conflicts = IndexRuleConflictDetector.Detect(new[]
        {
            Rule(@"**\node_modules\**", IndexRuleEffect.Exclude),
            Rule(@"**\node_modules\cache\**", IndexRuleEffect.Low),
        });

        Assert.HasCount(0, conflicts);
    }

    [TestMethod]
    public void MultiplePriorityRules_ReportEachIndexPair()
    {
        var conflicts = IndexRuleConflictDetector.Detect(new[]
        {
            Rule(@"C:\temp", IndexRuleEffect.Exclude),
            Rule(@"C:\temp\a", IndexRuleEffect.Low),
            Rule(@"C:\temp\b", IndexRuleEffect.High),
        });

        Assert.HasCount(2, conflicts);
        CollectionAssert.AreEqual(new[] { 1, 2 }, conflicts.Select(c => c.PriorityIndex).ToArray());
    }
}
