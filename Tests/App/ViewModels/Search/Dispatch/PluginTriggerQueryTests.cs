using Lertaro.App.ViewModels.Search.Dispatch;

namespace Lertaro.App.Tests.ViewModels.Search.Dispatch;

// Pins the activation rules of the plugin-trigger-word strip ("cs report" searches files for "report"):
// the whole first token must hit a declared keyword and be followed by a space, the rest -- trimmed --
// is what gets searched and highlighted. PluginTriggerQuery.Strip itself (which reads PluginManager) is
// deliberately not exercised here, same split as FileFilterScopeResolverTests.
[TestClass]
public sealed class PluginTriggerQueryTests
{
    private static readonly IReadOnlyList<string> Cs = ["cs"];
    private static readonly IReadOnlyList<string> Two = ["bb", "bh"];

    [TestMethod]
    public void Match_KeywordWithTerm_StripsDownToTheTerm()
    {
        Assert.IsTrue(PluginTriggerQuery.Match("cs report", Cs, out var remainder));
        Assert.AreEqual("report", remainder);
    }

    [TestMethod]
    public void Match_KeywordIsCaseInsensitive_TermKeepsItsOwnCase()
    {
        Assert.IsTrue(PluginTriggerQuery.Match("CS Report", Cs, out var remainder));
        Assert.AreEqual("Report", remainder);
    }

    [TestMethod]
    public void Match_LeadingSpaces_StillMatch()
    {
        Assert.IsTrue(PluginTriggerQuery.Match("   cs report", Cs, out var remainder));
        Assert.AreEqual("report", remainder);
    }

    [TestMethod]
    public void Match_MultipleTerms_KeepsThemAll()
    {
        Assert.IsTrue(PluginTriggerQuery.Match("cs quarterly report", Cs, out var remainder));
        Assert.AreEqual("quarterly report", remainder);
    }

    // One provider, several words (BrowserData's bookmark and history triggers) -- any of them claims it.
    [TestMethod]
    public void Match_SecondDeclaredKeyword_AlsoClaimsTheQuery()
    {
        Assert.IsTrue(PluginTriggerQuery.Match("bh site", Two, out var remainder));
        Assert.AreEqual("site", remainder);
    }

    // Typing the keyword alone is still a legitimate file search for that text: nothing is stripped, and
    // the provider that owns the word answers alongside the results rather than instead of them.
    [TestMethod]
    public void Match_BareKeywordWithNothingAfter_DoesNotStrip()
    {
        Assert.IsFalse(PluginTriggerQuery.Match("cs", Cs, out var remainder));
        Assert.AreEqual("cs", remainder);
    }

    // A trailing space is not a term. Stripping here would hand the engine an empty query, which the quick
    // window answers by clearing its results -- and the clear happens before the instant emission, so the
    // provider's own list would vanish on the keystroke that asks for it.
    [TestMethod]
    public void Match_KeywordWithTrailingSpaceOnly_DoesNotStrip()
    {
        Assert.IsFalse(PluginTriggerQuery.Match("cs ", Cs, out var remainder));
        Assert.AreEqual("cs ", remainder);
        Assert.IsFalse(PluginTriggerQuery.Match("cs    ", Cs, out remainder));
    }

    // Search actions contribute command words through the same collector ("mkdir sub" -> "sub").
    [TestMethod]
    public void Match_ActionCommandWordIsOneOfTheKeywords_StripAppliesToItToo()
    {
        IReadOnlyList<string> keywords = ["cs", "mkdir"];

        Assert.IsTrue(PluginTriggerQuery.Match("mkdir quarterly", keywords, out var remainder));
        Assert.AreEqual("quarterly", remainder);
    }

    // The whole first token has to be the keyword: a file called "csreport.docx" is still searchable, and
    // a keyword that merely starts a longer word must not swallow it.
    [TestMethod]
    public void Match_KeywordOnlyPartOfFirstToken_DoesNotStrip()
    {
        Assert.IsFalse(PluginTriggerQuery.Match("csreport draft", Cs, out var remainder));
        Assert.AreEqual("csreport draft", remainder);
    }

    [TestMethod]
    public void Match_NoProviderDeclaresAKeyword_NeverStrips()
    {
        Assert.IsFalse(PluginTriggerQuery.Match("cs report", Array.Empty<string>(), out var remainder));
        Assert.AreEqual("cs report", remainder);
    }

    [TestMethod]
    public void Match_EmptyQuery_LeavesItAlone()
    {
        Assert.IsFalse(PluginTriggerQuery.Match(string.Empty, Cs, out var remainder));
        Assert.AreEqual(string.Empty, remainder);
    }
}
