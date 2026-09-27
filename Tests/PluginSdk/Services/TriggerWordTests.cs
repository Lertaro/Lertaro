using Lertaro.PluginSdk.Services;

namespace Lertaro.PluginSdk.Tests.Services;

// The one tokenizing rule the search box uses for "a leading word plus what it carries" -- consulted by
// the host stripping a word off the file search, by an action dispatching on its command word, by a file
// filter's scope keyword, and by every instant provider re-recognising its own word. Each of those used to
// carry a private copy with its own idea of padding, casing and what counts as a separator, so the copies
// could disagree about the SAME typed text; these tests are the reason they can't drift again.
[TestClass]
public sealed class TriggerWordTests
{
    private const string Cs = "cs";

    [TestMethod]
    public void TryMatch_WordPlusTerm_ReturnsTheTerm()
    {
        Assert.IsTrue(TriggerWord.TryMatch("cs report", Cs, out var term));
        Assert.AreEqual("report", term);
    }

    [TestMethod]
    public void TryMatch_IsCaseInsensitive_AndKeepsTheTermsOwnCase()
    {
        Assert.IsTrue(TriggerWord.TryMatch("CS Report", "cs", out var term));
        Assert.AreEqual("Report", term);
    }

    [TestMethod]
    public void TryMatch_BareWord_MatchesWithEmptyArgument()
    {
        Assert.IsTrue(TriggerWord.TryMatch("cs", Cs, out var term));
        Assert.AreEqual(string.Empty, term);
        Assert.IsTrue(TriggerWord.TryMatch("cs   ", Cs, out term));
        Assert.AreEqual(string.Empty, term);
    }

    // "csreport draft" is a search for a file, not an invocation of "cs" -- the word has to be the WHOLE
    // first token, which is the boundary every copy of this rule used to draw differently.
    [TestMethod]
    public void TryMatch_WordOnlyPartOfFirstToken_DoesNotMatch()
    {
        Assert.IsFalse(TriggerWord.TryMatch("csreport draft", Cs, out _));
        Assert.IsFalse(TriggerWord.TryMatch("c report", Cs, out _));
    }

    [TestMethod]
    public void TryMatch_LeadingWhitespaceIsNotPartOfTheWord()
    {
        Assert.IsTrue(TriggerWord.TryMatch("   cs report", Cs, out var term));
        Assert.AreEqual("report", term);
    }

    // The three separators no copy used to accept: the ideographic space a Chinese IME emits in full-width
    // mode, a tab, and a non-breaking space from pasted text. All Unicode whitespace.
    [TestMethod]
    [DataRow("cs　report")]
    [DataRow("cs\treport")]
    [DataRow("cs report")]
    [DataRow("cs  report")]
    [DataRow("cs \t report")]
    public void TryMatch_AnyWhitespaceSeparatesWordFromTerm(string query)
    {
        Assert.IsTrue(TriggerWord.TryMatch(query, Cs, out var term), query);
        Assert.AreEqual("report", term);
    }

    [TestMethod]
    public void Normalize_TrimsEveryKindOfPadding()
    {
        Assert.AreEqual("cs", TriggerWord.Normalize("  cs　"));
        Assert.AreEqual(string.Empty, TriggerWord.Normalize(null));
        Assert.AreEqual(string.Empty, TriggerWord.Normalize(" 　\t"));
    }

    // A padded stored value is the hole that made the host and its providers disagree: the host trims the
    // word it strips, so a provider comparing against the untrimmed setting recognised nothing while the
    // word was still gone from the file search. Normalize closes it on the way in.
    [TestMethod]
    public void TryMatch_PaddedConfiguredWord_StillMatches()
    {
        Assert.IsTrue(TriggerWord.TryMatch("cs report", "  cs ", out var term));
        Assert.AreEqual("report", term);
    }

    [TestMethod]
    public void TryMatchInvoked_BareWordDoesNotInvoke_TermlessWordWithSeparatorDoes()
    {
        Assert.IsFalse(TriggerWord.TryMatchInvoked("cs", Cs, out _));
        Assert.IsTrue(TriggerWord.TryMatchInvoked("cs ", Cs, out var term));
        Assert.AreEqual(string.Empty, term, "the browse-all / placeholder view: invoked, nothing typed yet");
        Assert.IsTrue(TriggerWord.TryMatchInvoked("cs 路径", Cs, out term));
        Assert.AreEqual("路径", term);
    }

    // Leading whitespace alone must not count as the separator that commits a word -- " cs" is the word
    // typed, not the word invoked.
    [TestMethod]
    public void TryMatchInvoked_LeadingSpaceAloneDoesNotInvoke() =>
        Assert.IsFalse(TriggerWord.TryMatchInvoked(" cs", Cs, out _));

    [TestMethod]
    public void TryMatchAny_FirstMatchWinsAndReportsWhich()
    {
        IReadOnlyList<string> words = ["bb", "bh"];

        Assert.IsTrue(TriggerWord.TryMatchAny("bh site", words, out var matched, out var term));
        Assert.AreEqual("bh", matched);
        Assert.AreEqual("site", term);

        Assert.IsTrue(TriggerWord.TryMatchAny("bb", words, out matched, out term));
        Assert.AreEqual("bb", matched);
        Assert.AreEqual(string.Empty, term);

        Assert.IsFalse(TriggerWord.TryMatchAny("zz site", words, out _, out _));
        Assert.IsFalse(TriggerWord.TryMatchAny("cs x", Array.Empty<string>(), out _, out _));
    }

    [TestMethod]
    public void IsTypedPrefixOf_OffersAWordStillBeingTyped()
    {
        Assert.IsTrue(TriggerWord.IsTypedPrefixOf("m", "mkdir"));
        Assert.IsTrue(TriggerWord.IsTypedPrefixOf("MK", "mkdir"));
        Assert.IsTrue(TriggerWord.IsTypedPrefixOf("　mk　", "mkdir"), "padding around a still-being-typed word is not part of it");
    }

    // Once a separator is there the first token is final: "mi x" is a failed word, not a completion.
    // The whole word typed is not a prefix either -- that is TryMatch's case.
    [TestMethod]
    public void IsTypedPrefixOf_StopsOnceTheTokenIsFinal()
    {
        Assert.IsFalse(TriggerWord.IsTypedPrefixOf("mi x", "mkdir"));
        Assert.IsFalse(TriggerWord.IsTypedPrefixOf("mkdir", "mkdir"));
        Assert.IsFalse(TriggerWord.IsTypedPrefixOf(string.Empty, "mkdir"));
        Assert.IsFalse(TriggerWord.IsTypedPrefixOf("x", "mkdir"));
        Assert.IsFalse(TriggerWord.IsTypedPrefixOf("mi", "  "));
    }

    [TestMethod]
    public void TryMatch_EmptyOrMissingInputs_NeverMatch()
    {
        Assert.IsFalse(TriggerWord.TryMatch(string.Empty, Cs, out _));
        Assert.IsFalse(TriggerWord.TryMatch("cs report", string.Empty, out _));
        Assert.IsFalse(TriggerWord.TryMatch("cs report", null, out _));
        Assert.IsFalse(TriggerWord.TryMatch("x", " 　 ", out _));
    }
}
