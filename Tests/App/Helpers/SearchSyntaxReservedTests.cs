using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

// The characters the search box reads as syntax, and the rule that keeps the configurable leading-trigger
// surfaces (a plugin query token prefix, an instant-answer keyword, a per-result-type trigger) from
// claiming one of them.
//
// The failure this prevents is always silent: the syntax consumes the leading character before the
// trigger is ever consulted, so the feature simply stops responding with nothing on screen to explain it.
[TestClass]
public sealed class SearchSyntaxReservedTests
{
    [TestMethod]
    [DataRow("\\audio")]
    [DataRow("<s")]
    [DataRow(">s")]
    [DataRow(":temp")]
    [DataRow("*node_modules")]
    public void StartsWithReservedCharacter_EverySyntaxCharacter_IsReserved(string value)
        => Assert.IsTrue(SearchSyntaxReserved.StartsWithReservedCharacter(value));

    [TestMethod]
    [DataRow("audio")]
    [DataRow("set")]
    [DataRow("bb")]
    [DataRow("g")]
    [DataRow("#cmd")]
    [DataRow("$cmd")]
    [DataRow("%path%")]
    public void StartsWithReservedCharacter_OrdinaryTriggerWords_AreUsable(string value)
        => Assert.IsFalse(SearchSyntaxReserved.StartsWithReservedCharacter(value));

    [TestMethod]
    public void ValidateLeadingCharacter_ReservedFirstCharacter_IsReported()
    {
        // Only that a message is produced: the translated text itself is not loaded in this assembly (the
        // lookups fall back to "[Key]"), so asserting on its wording would test TranslationManager's load
        // state rather than this rule. The characters are named by DescribeLeadingCharacters, covered below.
        Assert.IsNotNull(SearchSyntaxReserved.ValidateLeadingCharacter(":tr"));
        Assert.IsFalse(string.IsNullOrEmpty(SearchSyntaxReserved.ValidateLeadingCharacter("\\tr")));
    }

    [TestMethod]
    public void ValidateLeadingCharacter_Empty_IsReported()
        => Assert.IsNotNull(SearchSyntaxReserved.ValidateLeadingCharacter(string.Empty));

    [TestMethod]
    public void ValidateLeadingCharacter_OrdinaryWord_IsAccepted()
    {
        Assert.IsNull(SearchSyntaxReserved.ValidateLeadingCharacter("tr"));
        Assert.IsNull(SearchSyntaxReserved.ValidateLeadingCharacter("#cmd"));
    }

    [TestMethod]
    public void ValidateLeadingCharacter_OnlyTheFirstCharacterIsJudged()
    {
        // A keyword is matched against the START of the query, so that is the only position the syntax
        // competes for -- a later reserved character is ordinary text and must not be rejected.
        Assert.IsNull(SearchSyntaxReserved.ValidateLeadingCharacter("my-trigger"));
        Assert.IsNull(SearchSyntaxReserved.ValidateLeadingCharacter("a:b"));
    }

    [TestMethod]
    public void DescribeLeadingCharacters_ListsEveryReservedCharacter()
    {
        var described = SearchSyntaxReserved.DescribeLeadingCharacters();

        foreach (var c in SearchSyntaxReserved.LeadingCharacters)
            Assert.Contains(c.ToString(), described);
    }
}
