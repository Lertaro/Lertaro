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
    [DataRow("/\\.md$/")]
    public void StartsWithReservedCharacter_EverySyntaxCharacter_IsReserved(string value)
        => Assert.IsTrue(SearchSyntaxReserved.StartsWithReservedCharacter(value));

    [TestMethod]
    [DataRow("audio")]
    [DataRow("set")]
    [DataRow("bb")]
    [DataRow("g")]
    public void StartsWithReservedCharacter_OrdinaryTriggerWords_AreUsable(string value)
        => Assert.IsFalse(SearchSyntaxReserved.StartsWithReservedCharacter(value));

    // A provider's own hardcoded leading character is not syntax, but it is just as unavailable to another
    // first-character trigger: the input would be answered by two features at once.
    [TestMethod]
    [DataRow("#dir")]
    [DataRow("$dir")]
    [DataRow("%TE")]
    public void ValidateLeadingCharacter_ProviderClaimedCharacter_IsReported(string value)
    {
        Assert.IsNotNull(SearchSyntaxReserved.ValidateLeadingCharacter(value));
        Assert.IsTrue(SearchSyntaxReserved.IsProviderClaimed(value[0]));
    }

    [TestMethod]
    [DataRow('#')]
    [DataRow('$')]
    [DataRow('%')]
    public void IsProviderClaimed_TheInstantAnswerCharacters_AreClaimed(char value)
        => Assert.IsTrue(SearchSyntaxReserved.IsProviderClaimed(value));

    [TestMethod]
    [DataRow('#')]
    [DataRow('$')]
    [DataRow('%')]
    public void IsReserved_TheProviderClaimedCharacters_AreNotSyntax(char value)
    {
        // They belong to a provider, not to the grammar -- the two lists answer different questions and the
        // message shown to the user differs, so a character must not drift between them.
        Assert.IsFalse(SearchSyntaxReserved.IsReserved(value));
    }

    [TestMethod]
    public void ValidateLeadingCharacter_ReservedWinsOverProviderClaimed()
    {
        // No overlap today; if one is ever introduced, the syntax message must still be the one shown.
        foreach (var c in SearchSyntaxReserved.LeadingCharacters)
            Assert.IsFalse(SearchSyntaxReserved.IsProviderClaimed(c), c.ToString());
    }

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
        => Assert.IsNull(SearchSyntaxReserved.ValidateLeadingCharacter("tr"));

    [TestMethod]
    public void DescribeProviderClaimedCharacters_ListsTheInstantAnswerCharacters()
    {
        var described = SearchSyntaxReserved.DescribeProviderClaimedCharacters();

        foreach (var c in SearchSyntaxReserved.ProviderClaimedCharacters)
            Assert.Contains(c.ToString(), described);
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

    [TestMethod]
    public void IsUnusableAsTokenPrefix_TheTokenPrefixItself_IsUsable()
    {
        // The one exception, and it is not arbitrary: '\' is listed as reserved because the scanner reads
        // it as a token start -- but only when it was handed '\' as the prefix, which is exactly what the
        // prefix fields configure. Every other character in that list is consumed regardless of them.
        Assert.IsTrue(SearchSyntaxReserved.IsReserved(SearchSyntaxReserved.TokenPrefixCharacter));
        Assert.IsFalse(SearchSyntaxReserved.IsUnusableAsTokenPrefix(SearchSyntaxReserved.TokenPrefixCharacter));
    }

    [TestMethod]
    [DataRow('<')]
    [DataRow('>')]
    [DataRow(':')]
    [DataRow('*')]
    public void IsUnusableAsTokenPrefix_TheOtherSyntaxCharacters_AreReported(char value)
        => Assert.IsTrue(SearchSyntaxReserved.IsUnusableAsTokenPrefix(value));

    [TestMethod]
    public void IsUnusableAsTokenPrefix_Slash_IsReported()
    {
        // '/' is not an always-on trigger like '<'/'>' -- it only opens a clause when the word also closes
        // with one -- but a '/' prefix still breaks regex queries outright, because QueryTokenScanner
        // compares the prefix against the first character alone and so lifts the whole clause out as a
        // token before RegexQueryParser runs. See QueryTokenScannerTests.Scan_SlashPrefix_EatsTheRegexClause.
        Assert.IsTrue(SearchSyntaxReserved.IsReserved('/'));
        Assert.IsTrue(SearchSyntaxReserved.IsUnusableAsTokenPrefix('/'));
        Assert.IsNotNull(SearchSyntaxReserved.ValidateLeadingCharacter("/\\.md$/"));
    }

    [TestMethod]
    [DataRow('a')]
    [DataRow('!')]
    public void IsUnusableAsTokenPrefix_OrdinaryCharacters_AreUsable(char value)
        => Assert.IsFalse(SearchSyntaxReserved.IsUnusableAsTokenPrefix(value));
}
