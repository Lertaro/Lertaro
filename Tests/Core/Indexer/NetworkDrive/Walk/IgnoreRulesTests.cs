using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

[TestClass]
public sealed class IgnoreRulesTests
{
    [TestMethod]
    public void Parse_BlankLine_ReturnsNull() => Assert.IsNull(NetworkIgnoreRule.Parse(@"c:\repo\", "   "));

    [TestMethod]
    public void Parse_CommentLine_ReturnsNull() => Assert.IsNull(NetworkIgnoreRule.Parse(@"c:\repo\", "# a comment"));

    [TestMethod]
    public void Parse_NegatedPattern_SetsNegatedFlag()
    {
        var rule = NetworkIgnoreRule.Parse(@"c:\repo\", "!important.log");

        Assert.IsTrue(rule!.Value.Negated);
        Assert.AreEqual("important.log", rule.Value.Pattern);
    }

    [TestMethod]
    public void Parse_TrailingSlash_SetsDirectoryOnlyAndStripsSlash()
    {
        var rule = NetworkIgnoreRule.Parse(@"c:\repo\", "build/");

        Assert.IsTrue(rule!.Value.DirectoryOnly);
        Assert.AreEqual("build", rule.Value.Pattern);
    }

    [TestMethod]
    public void Parse_LeadingSlash_SetsAnchoredAndStripsSlash()
    {
        var rule = NetworkIgnoreRule.Parse(@"c:\repo\", "/build");

        Assert.IsTrue(rule!.Value.Anchored);
        Assert.AreEqual("build", rule.Value.Pattern);
    }

    [TestMethod]
    public void Matches_SimplePatternUnderBasePath_Matches()
    {
        var rule = NetworkIgnoreRule.Parse(@"c:\repo\", "*.log")!.Value;

        Assert.IsTrue(rule.Matches(@"c:\repo\output.log", "output.log", isDirectory: false));
    }

    [TestMethod]
    public void Matches_OutsideBasePath_DoesNotMatch()
    {
        var rule = NetworkIgnoreRule.Parse(@"c:\repo\", "*.log")!.Value;

        Assert.IsFalse(rule.Matches(@"c:\other\output.log", "output.log", isDirectory: false));
    }

    [TestMethod]
    public void Matches_DirectoryOnlyRuleAgainstFile_DoesNotMatch()
    {
        var rule = NetworkIgnoreRule.Parse(@"c:\repo\", "build/")!.Value;

        Assert.IsFalse(rule.Matches(@"c:\repo\build", "build", isDirectory: false));
        Assert.IsTrue(rule.Matches(@"c:\repo\build", "build", isDirectory: true));
    }

    [TestMethod]
    public void IsIgnored_NoRules_ReturnsFalse() => Assert.IsFalse(NetworkIgnoreRuleSet.Empty.IsIgnored(@"c:\repo\file.txt", "file.txt", isDirectory: false));

    [TestMethod]
    public void IsIgnored_MatchingRule_ReturnsTrue()
    {
        var set = NetworkIgnoreRuleSet.Empty.Add(NetworkIgnoreRule.Parse(@"c:\repo\", "*.log")!.Value);

        Assert.IsTrue(set.IsIgnored(@"c:\repo\output.log", "output.log", isDirectory: false));
    }

    [TestMethod]
    public void IsIgnored_LaterNegationRule_OverridesEarlierMatch()
    {
        // gitignore semantics: rules apply in order, and a later negated rule can re-include a path an
        // earlier rule excluded.
        var set = NetworkIgnoreRuleSet.Empty
            .Add(NetworkIgnoreRule.Parse(@"c:\repo\", "*.log")!.Value)
            .Add(NetworkIgnoreRule.Parse(@"c:\repo\", "!important.log")!.Value);

        Assert.IsFalse(set.IsIgnored(@"c:\repo\important.log", "important.log", isDirectory: false));
        Assert.IsTrue(set.IsIgnored(@"c:\repo\other.log", "other.log", isDirectory: false));
    }
}
