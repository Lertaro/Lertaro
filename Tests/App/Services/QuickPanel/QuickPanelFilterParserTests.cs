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
        var spec = QuickPanelFilterParser.Parse(@"*.lnk;\doc;\img");

        CollectionAssert.AreEqual(new[] { "*.lnk" }, spec.GlobPatterns);
        CollectionAssert.AreEqual(new[] { @"\doc", @"\img" }, spec.TokenFilters);
    }

    [TestMethod]
    public void Parse_PipeToken_IsKeptAsOneFilter()
    {
        var spec = QuickPanelFilterParser.Parse(@"*.lnk;\doc|img");

        CollectionAssert.AreEqual(new[] { "*.lnk" }, spec.GlobPatterns);
        CollectionAssert.AreEqual(new[] { @"\doc|img" }, spec.TokenFilters);
    }

    [TestMethod]
    public void Parse_InvalidTokenEntry_IsNotToken()
    {
        // Empty keyword after the pipe is invalid syntax, so the whole entry falls back to glob
        // (where the backslash can never match a real file name).
        var spec = QuickPanelFilterParser.Parse(@"\doc|");

        Assert.HasCount(0, spec.TokenFilters);
        CollectionAssert.AreEqual(new[] { @"\doc|" }, spec.GlobPatterns);
    }

    [TestMethod]
    public void Parse_OldAtMarkerForm_IsMigratedToTheCurrentToken()
    {
        // ":@doc" was the old spelling (a ':' prefix plus an '@' category marker). The marker is gone from
        // the grammar, but this test used to assert that the entry is "just an unmatched glob" -- and that is
        // not a benign outcome: a source whose only positive entry cannot match enumerates nothing, so an
        // upgraded install lost the tab's contents with no message. The spelling is now read as the token it
        // meant; Parse_LegacyAtForms_AllSpellingsResolveToTheCurrentToken covers the rest.
        var spec = QuickPanelFilterParser.Parse(":@doc");

        CollectionAssert.AreEqual(new[] { @"\doc" }, spec.TokenFilters);
        Assert.HasCount(0, spec.GlobPatterns);
    }

    [TestMethod]
    public void Parse_ColonPrefixedEntry_IsNotAToken()
    {
        // ':' is the exclusion operator in the search box, never a token prefix.
        var spec = QuickPanelFilterParser.Parse(":.pdf");

        Assert.HasCount(0, spec.TokenFilters);
        CollectionAssert.AreEqual(new[] { ":.pdf" }, spec.GlobPatterns);
    }

    [TestMethod]
    public void Parse_DuplicateTokens_FirstWins()
    {
        var spec = QuickPanelFilterParser.Parse(@"\doc;\doc");

        CollectionAssert.AreEqual(new[] { @"\doc" }, spec.TokenFilters);
    }

    [TestMethod]
    public void Parse_DuplicateKeywordsInsideOneToken_FirstWins()
    {
        var spec = QuickPanelFilterParser.Parse(@"\doc|doc");

        CollectionAssert.AreEqual(new[] { @"\doc" }, spec.TokenFilters);
    }

    [TestMethod]
    public void Parse_CustomGlobalTokenPrefix_IsUsed()
    {
        var spec = QuickPanelFilterParser.Parse("*.lnk;#doc", globalTokenPrefix: '#');

        CollectionAssert.AreEqual(new[] { "*.lnk" }, spec.GlobPatterns);
        CollectionAssert.AreEqual(new[] { "#doc" }, spec.TokenFilters);
    }

    [TestMethod]
    public void Parse_NegatedGlob_GoesToExcluded()
    {
        var spec = QuickPanelFilterParser.Parse("*.lnk;!*.desktop.ini");

        CollectionAssert.AreEqual(new[] { "*.lnk" }, spec.GlobPatterns);
        Assert.HasCount(0, spec.TokenFilters);
        CollectionAssert.AreEqual(new[] { "*.desktop.ini" }, spec.ExcludedGlobPatterns);
    }

    [TestMethod]
    public void Parse_ExclusionOnly_HasNoPositiveGlobs()
    {
        var spec = QuickPanelFilterParser.Parse("!desktop.ini");

        Assert.HasCount(0, spec.GlobPatterns);
        CollectionAssert.AreEqual(new[] { "desktop.ini" }, spec.ExcludedGlobPatterns);
        Assert.IsFalse(spec.IsMatchAll);
        Assert.IsTrue(spec.NeedsPostFilter);
    }

    [TestMethod]
    public void Parse_BareNegation_IsIgnored()
    {
        var spec = QuickPanelFilterParser.Parse("!");

        Assert.IsTrue(spec.IsMatchAll);
    }

    [TestMethod]
    public void Parse_DuplicateNegatedGlobs_FirstWins()
    {
        var spec = QuickPanelFilterParser.Parse("!*.tmp;!*.tmp");

        CollectionAssert.AreEqual(new[] { "*.tmp" }, spec.ExcludedGlobPatterns);
    }

    [TestMethod]
    public void Parse_LegacyAtForms_AllSpellingsResolveToTheCurrentToken()
    {
        foreach (var legacy in new[] { ":@doc|img", "@doc|img" })
        {
            var spec = QuickPanelFilterParser.Parse(legacy);

            CollectionAssert.AreEqual(new[] { @"\doc|img" }, spec.TokenFilters, legacy);
            Assert.IsEmpty(spec.GlobPatterns, legacy);
        }
    }

    [TestMethod]
    public void Parse_AtInsideARealGlob_IsLeftAlone()
    {
        // Only a bare keyword list after the marker is a retired token. Rewriting anything with an '@' in it
        // would turn a user's path or e-mail-shaped glob into a token nobody claims, which empties the
        // source -- the exact failure the migration above exists to prevent.
        foreach (var glob in new[] { "mail@*", "*@acme*", @"C:\team\bob@acme\*" })
        {
            var spec = QuickPanelFilterParser.Parse(glob);

            Assert.IsEmpty(spec.TokenFilters, glob);
            CollectionAssert.AreEqual(new[] { glob }, spec.GlobPatterns, glob);
        }
    }
}
