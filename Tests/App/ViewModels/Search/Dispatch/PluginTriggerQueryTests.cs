using System.IO;
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
    private static PluginTriggerQuery.Entry E(string word, string owner, bool strips = true, string ownerId = "") =>
        new(word, owner, strips, ownerId);

    // A full-width or tab separator has to work on this side too: the provider that owns the word sees the
    // untouched box text, so if only one of the two accepted "cs　report" the word would be stripped from
    // the file search while the provider stayed silent (or the reverse).
    [TestMethod]
    public void Match_FullWidthOrTabSeparator_StripsDownToTheTerm()
    {
        Assert.IsTrue(PluginTriggerQuery.Match("cs　report", Cs, out var remainder));
        Assert.AreEqual("report", remainder);

        Assert.IsTrue(PluginTriggerQuery.Match("cs\treport", Cs, out remainder));
        Assert.AreEqual("report", remainder);
    }

    [TestMethod]
    public void Match_KeywordStoredWithPadding_StillStrips()
    {
        Assert.IsTrue(PluginTriggerQuery.Match("cs report", ["  cs  "], out var remainder));
        Assert.AreEqual("report", remainder);
    }

    // A separator alone is not a term: "cs " must not leave an empty query for the engine.
    [TestMethod]
    public void Match_KeywordWithFullWidthSpaceOnly_DoesNotStrip()
    {
        Assert.IsFalse(PluginTriggerQuery.Match("cs　", Cs, out var remainder));
        Assert.AreEqual("cs　", remainder);
    }

    // A per-type trigger character only ever inspects ONE character, so a registered word has to win the
    // token first: with "s" assigned to a type, "set 路径" would otherwise be cut down to "et 路径" and the
    // file search would run on text its owner never typed.
    [TestMethod]
    public void ClaimsLeadingWord_MultiCharacterWordOnTheToken_ClaimsIt()
    {
        Assert.IsTrue(PluginTriggerQuery.ClaimsLeadingWord("set 路径", [E("set", "核心扩展")]));
        Assert.IsTrue(PluginTriggerQuery.ClaimsLeadingWord("SET 路径", [E("set", "核心扩展")]), "case-insensitive like every other keyword comparison");
        Assert.IsTrue(PluginTriggerQuery.ClaimsLeadingWord("set", [E("set", "核心扩展")]), "the bare word is claimed too -- the character would cut it in half");
        Assert.IsTrue(PluginTriggerQuery.ClaimsLeadingWord("  set 路径", [E("  set ", "核心扩展")]), "a padded stored word still claims");
    }

    // A single-character entry IS the type-trigger family this guards against: letting it claim the token
    // would put the character strip back exactly where it was.
    [TestMethod]
    public void ClaimsLeadingWord_SingleCharacterEntryNeverClaims()
        => Assert.IsFalse(PluginTriggerQuery.ClaimsLeadingWord("s 路径", [E("s", "某插件")]));

    [TestMethod]
    public void ClaimsLeadingWord_WholeTokenOnly()
    {
        Assert.IsFalse(PluginTriggerQuery.ClaimsLeadingWord("setreport draft", [E("set", "核心扩展")]));
        Assert.IsFalse(PluginTriggerQuery.ClaimsLeadingWord(string.Empty, [E("set", "核心扩展")]));
        Assert.IsFalse(PluginTriggerQuery.ClaimsLeadingWord("set 路径", Array.Empty<PluginTriggerQuery.Entry>()));
    }

    // Wiring guard. The rule behind which command words exist in a window is PluginManager.ActionsVisibleIn
    // (IsVisibleInSearch plus the component filter, pinned by the plugin-side tests); what cannot be executed
    // here is the call that makes it apply -- and one edit back to the window-agnostic Actions silently
    // returns the inline-only mkdir/touch/cmd words to eating the quick window's first token.
    [TestMethod]
    public void TheActionInventoryIsAskedForTheCallersWindow()
    {
        var source = Source("App/ViewModels/Search/Dispatch/PluginTriggerQuery.cs");

        Assert.Contains("PluginManager.Instance.ActionsVisibleIn(type)", source,
            "the action words have to come from the window-aware gate");
        Assert.Contains(": PluginManager.Instance.Actions)", source,
            "and the window-agnostic branch has to keep every action, for the Settings warning");
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return dir!.FullName;
    }
}
