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
    [DataRow("?report")]
    // NOTE: this list mirrors LeadingCharacters rather than the syntax table in Plan.md §2.2, which also
    // lists the quote pair (' ") as grouping characters. Note the two disagree: a leading quote changes how
    // the query is read (see FzfPatternParser.MergeQuotedPhrases) but is not refused to a configurable
    // trigger, so a row for it here would fail. This test cannot tell the two sets apart -- if the grouping
    // characters are ever promoted to reserved, this DataRow set and LeadingCharacters move together.
    public void IsReserved_EverySyntaxCharacter_IsReserved(string value)
        => Assert.IsTrue(SearchSyntaxReserved.IsReserved(value[0]));

    [TestMethod]
    [DataRow("audio")]
    [DataRow("set")]
    [DataRow("bb")]
    [DataRow("g")]
    public void IsReserved_OrdinaryTriggerWords_AreUsable(string value)
        => Assert.IsFalse(SearchSyntaxReserved.IsReserved(value[0]));

    // A provider's own hardcoded leading character is not syntax, but it is just as unavailable to another
    // first-character trigger: the input would be answered by two features at once.
    [TestMethod]
    [DataRow("#dir")]
    [DataRow("$dir")]
    [DataRow("%TE")]
    public void ValidateLeadingCharacter_ProviderClaimedCharacter_IsReported(string value)
    {
        Assert.IsNotNull(SearchSyntaxReserved.ValidateLeadingCharacter(value));
        Assert.Contains(value[0], SearchSyntaxReserved.ProviderClaimedCharacters);
    }

    [TestMethod]
    [DataRow('#')]
    [DataRow('$')]
    [DataRow('%')]
    public void ProviderClaimedCharacters_TheInstantAnswerCharacters_AreClaimed(char value)
        => Assert.Contains(value, SearchSyntaxReserved.ProviderClaimedCharacters);

    [TestMethod]
    [DataRow('#')]
    [DataRow('$')]
    [DataRow('%')]
    // They belong to a provider, not to the grammar -- the two lists answer different questions and the
    // message shown to the user differs, so a character must not drift between them.
    public void IsReserved_TheProviderClaimedCharacters_AreNotSyntax(char value)
        => Assert.IsFalse(SearchSyntaxReserved.IsReserved(value));

    [TestMethod]
    public void ValidateLeadingCharacter_ReservedWinsOverProviderClaimed()
    {
        // No overlap today; if one is ever introduced, the syntax message must still be the one shown.
        foreach (var c in SearchSyntaxReserved.LeadingCharacters)
            Assert.DoesNotContain(c, SearchSyntaxReserved.ProviderClaimedCharacters);
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
    [DataRow('?')]
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
    public void IsUnusableAsTokenPrefix_PrecisionInversion_IsReported()
    {
        // Unlike '/', a '?' prefix would not break tokenization at all: QueryTokenScanner compares the
        // prefix against the first character alone, so "?audio" would still be lifted and handed to the
        // plugin. What it would break is quiet -- every word that prefix lifts stops being read for the
        // inversion trigger (see TermTriggers), so the user loses the syntax with nothing on screen to
        // explain it. That is the failure this list exists to catch, so it is refused.
        Assert.IsTrue(SearchSyntaxReserved.IsReserved(SearchSyntaxReserved.PrecisionInversionCharacter));
        Assert.IsTrue(SearchSyntaxReserved.IsUnusableAsTokenPrefix(SearchSyntaxReserved.PrecisionInversionCharacter));
        Assert.IsNotNull(SearchSyntaxReserved.ValidateLeadingCharacter("?report"));
    }

    [TestMethod]
    public void PrecisionInversionCharacter_IsListedAmongTheLeadingCharacters()
    {
        // The constant and the list have to move together: the constant is what the parser and the docs
        // name, the list is what these checks consult, and one without the other silently drops the rule.
        Assert.Contains(SearchSyntaxReserved.PrecisionInversionCharacter, SearchSyntaxReserved.LeadingCharacters);
        Assert.Contains("?", SearchSyntaxReserved.DescribeLeadingCharacters());
    }

    // Two sentences, two sets. "Which characters may not start a trigger" is not the same question as
    // "which characters may not be a token PREFIX" (the prefix is not reserved against itself), and using
    // one list for both is how the shipped help text came to name the '\' it defaults to.
    [TestMethod]
    public void UnusableTokenPrefixCharacters_IsTheReservedSetWithoutThePrefixItself()
    {
        var usable = SearchSyntaxReserved.UnusableTokenPrefixCharacters;

        Assert.DoesNotContain(SearchSyntaxReserved.TokenPrefixCharacter, usable);
        Assert.HasCount(SearchSyntaxReserved.LeadingCharacters.Count - 1, usable);
        foreach (var c in SearchSyntaxReserved.LeadingCharacters)
        {
            if (c != SearchSyntaxReserved.TokenPrefixCharacter)
                Assert.Contains(c, usable);
        }

        // Every one of them really is refused, which is what the help text claims.
        foreach (var c in usable)
            Assert.IsTrue(SearchSyntaxReserved.IsUnusableAsTokenPrefix(c));
    }

    [TestMethod]
    public void DescribeUnusableTokenPrefixCharacters_NamesEveryRefusedCharacterAndNotThePrefixItself()
    {
        var described = SearchSyntaxReserved.DescribeUnusableTokenPrefixCharacters();

        foreach (var c in SearchSyntaxReserved.UnusableTokenPrefixCharacters)
            Assert.Contains(c.ToString(), described);
        Assert.DoesNotContain(SearchSyntaxReserved.TokenPrefixCharacter.ToString(), described);
    }

    [TestMethod]
    [DataRow('a')]
    [DataRow('!')]
    public void IsUnusableAsTokenPrefix_OrdinaryCharacters_AreUsable(char value)
        => Assert.IsFalse(SearchSyntaxReserved.IsUnusableAsTokenPrefix(value));
}
