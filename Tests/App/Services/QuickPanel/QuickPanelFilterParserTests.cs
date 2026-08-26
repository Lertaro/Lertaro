using Lertaro.App.Services.QuickPanel;

namespace Lertaro.App.Tests.Services.QuickPanel;

[TestClass]
public sealed class QuickPanelFilterParserTests
{
    [TestMethod]
    public void Parse_EmptyFilter_ReturnsMatchAllGlob()
    {
        var spec = QuickPanelFilterParser.Parse("");

        Assert.HasCount(1, spec.GlobPatterns);
        Assert.AreEqual("*", spec.GlobPatterns[0]);
        Assert.HasCount(0, spec.TokenFilters);
    }

    [TestMethod]
    public void Parse_GlobOnly_ReturnsGlobs()
    {
        var spec = QuickPanelFilterParser.Parse("*.mp4;*.mkv");

        CollectionAssert.AreEqual(new[] { "*.mp4", "*.mkv" }, spec.GlobPatterns);
        Assert.HasCount(0, spec.TokenFilters);
    }

    [TestMethod]
    public void Parse_TokenAndGlob_SeparatesThem()
    {
        var spec = QuickPanelFilterParser.Parse("*.lnk;:@doc;:@img");

        CollectionAssert.AreEqual(new[] { "*.lnk" }, spec.GlobPatterns);
        CollectionAssert.AreEqual(new[] { "@doc", "@img" }, spec.TokenFilters);
    }

    [TestMethod]
    public void Parse_PipeToken_IsKeptAsOneFilter()
    {
        var spec = QuickPanelFilterParser.Parse("*.lnk;:@doc|img");

        CollectionAssert.AreEqual(new[] { "*.lnk" }, spec.GlobPatterns);
        CollectionAssert.AreEqual(new[] { "@doc|img" }, spec.TokenFilters);
    }

    [TestMethod]
    public void Parse_InvalidTokenEntry_IsNotToken()
    {
        // Empty keyword after the pipe is invalid syntax, so the whole entry falls back to glob
        // (where the colon can never match a real file name).
        var spec = QuickPanelFilterParser.Parse(":@doc|");

        Assert.HasCount(0, spec.TokenFilters);
        CollectionAssert.AreEqual(new[] { ":@doc|" }, spec.GlobPatterns);
    }

    [TestMethod]
    public void Parse_NonAtColonEntry_IsNotToken()
    {
        var spec = QuickPanelFilterParser.Parse(":.pdf");

        Assert.HasCount(0, spec.TokenFilters);
        CollectionAssert.AreEqual(new[] { ":.pdf" }, spec.GlobPatterns);
    }

    [TestMethod]
    public void Parse_DuplicateTokens_FirstWins()
    {
        var spec = QuickPanelFilterParser.Parse(":@doc;:@doc");

        CollectionAssert.AreEqual(new[] { "@doc" }, spec.TokenFilters);
    }

    [TestMethod]
    public void Parse_DuplicateKeywordsInsideOneToken_FirstWins()
    {
        var spec = QuickPanelFilterParser.Parse(":@doc|doc");

        CollectionAssert.AreEqual(new[] { "@doc" }, spec.TokenFilters);
    }

    [TestMethod]
    public void Parse_CustomGlobalTokenPrefix_IsUsed()
    {
        var spec = QuickPanelFilterParser.Parse("*.lnk;#@doc", globalTokenPrefix: '#');

        CollectionAssert.AreEqual(new[] { "*.lnk" }, spec.GlobPatterns);
        CollectionAssert.AreEqual(new[] { "@doc" }, spec.TokenFilters);
    }
}
