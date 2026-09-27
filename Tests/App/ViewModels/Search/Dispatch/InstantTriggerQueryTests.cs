using Lertaro.App.ViewModels.Search.Dispatch;

namespace Lertaro.App.Tests.ViewModels.Search.Dispatch;

// Pins the activation rules of the plugin-trigger-word strip ("cs report" searches files for "report"):
// the whole first token must hit a declared keyword and be followed by a space, the rest -- trimmed --
// is what gets searched and highlighted. InstantTriggerQuery.Strip itself (which reads PluginManager) is
// deliberately not exercised here, same split as FileFilterScopeResolverTests.
[TestClass]
public sealed class InstantTriggerQueryTests
{
    private static readonly IReadOnlyList<string> Cs = ["cs"];
    private static readonly IReadOnlyList<string> Two = ["bb", "bh"];

    [TestMethod]
    public void Match_KeywordWithTerm_StripsDownToTheTerm()
    {
        Assert.IsTrue(InstantTriggerQuery.Match("cs report", Cs, out var remainder));
        Assert.AreEqual("report", remainder);
    }

    [TestMethod]
    public void Match_KeywordIsCaseInsensitive_TermKeepsItsOwnCase()
    {
        Assert.IsTrue(InstantTriggerQuery.Match("CS Report", Cs, out var remainder));
        Assert.AreEqual("Report", remainder);
    }

    [TestMethod]
    public void Match_LeadingSpaces_StillMatch()
    {
        Assert.IsTrue(InstantTriggerQuery.Match("   cs report", Cs, out var remainder));
        Assert.AreEqual("report", remainder);
    }

    [TestMethod]
    public void Match_MultipleTerms_KeepsThemAll()
    {
        Assert.IsTrue(InstantTriggerQuery.Match("cs quarterly report", Cs, out var remainder));
        Assert.AreEqual("quarterly report", remainder);
    }

    // One provider, several words (BrowserData's bookmark and history triggers) -- any of them claims it.
    [TestMethod]
    public void Match_SecondDeclaredKeyword_AlsoClaimsTheQuery()
    {
        Assert.IsTrue(InstantTriggerQuery.Match("bh site", Two, out var remainder));
        Assert.AreEqual("site", remainder);
    }

    // Typing the keyword alone is still a legitimate file search for that text: nothing is stripped, and
    // the provider that owns the word answers alongside the results rather than instead of them.
    [TestMethod]
    public void Match_BareKeywordWithNothingAfter_DoesNotStrip()
    {
        Assert.IsFalse(InstantTriggerQuery.Match("cs", Cs, out var remainder));
        Assert.AreEqual("cs", remainder);
    }

    // The space is what says "I am invoking the provider": the term just has not been typed yet, so this
    // strips to empty, which every caller already treats as "keep typing" rather than as a search.
    [TestMethod]
    public void Match_KeywordWithTrailingSpaceOnly_StripsToEmpty()
    {
        Assert.IsTrue(InstantTriggerQuery.Match("cs ", Cs, out var remainder));
        Assert.AreEqual(string.Empty, remainder);
    }

    // The whole first token has to be the keyword: a file called "csreport.docx" is still searchable, and
    // a keyword that merely starts a longer word must not swallow it.
    [TestMethod]
    public void Match_KeywordOnlyPartOfFirstToken_DoesNotStrip()
    {
        Assert.IsFalse(InstantTriggerQuery.Match("csreport draft", Cs, out var remainder));
        Assert.AreEqual("csreport draft", remainder);
    }

    [TestMethod]
    public void Match_NoProviderDeclaresAKeyword_NeverStrips()
    {
        Assert.IsFalse(InstantTriggerQuery.Match("cs report", Array.Empty<string>(), out var remainder));
        Assert.AreEqual("cs report", remainder);
    }

    [TestMethod]
    public void Match_EmptyQuery_LeavesItAlone()
    {
        Assert.IsFalse(InstantTriggerQuery.Match(string.Empty, Cs, out var remainder));
        Assert.AreEqual(string.Empty, remainder);
    }
}
