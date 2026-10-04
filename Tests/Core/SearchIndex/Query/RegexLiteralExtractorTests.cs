using Lertaro.Core.SearchIndex.Query;

namespace Lertaro.Core.Tests.SearchIndex.Query;

[TestClass]
public sealed class RegexLiteralExtractorTests
{
    [TestMethod]
    public void Extract_AlternationWithClasses_KeepsOnlyTheLiteralRun() => Assert.AreEqual("ab", RegexLiteralExtractor.ExtractRequiredLiteral("^ab.c\\..{3}$"));

    [TestMethod]
    public void Extract_ExtensionAlternation_StillFindsTheRequiredPrefix()
    {
        Assert.AreEqual(".", RegexLiteralExtractor.ExtractRequiredLiteral("\\.(?:ogg|mp3)$"));
        Assert.AreEqual(".", RegexLiteralExtractor.ExtractRequiredLiteral("\\.(?:ogg|mp3|wav|flac)$"));
    }

    [TestMethod]
    public void Extract_DigitClassContributesNothing() => Assert.AreEqual("_", RegexLiteralExtractor.ExtractRequiredLiteral("^[0-9]{8}_"));

    [TestMethod]
    public void Extract_NoLiteralAtAll_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, RegexLiteralExtractor.ExtractRequiredLiteral(".*"));
        Assert.AreEqual(string.Empty, RegexLiteralExtractor.ExtractRequiredLiteral("^.{5}$"));
        Assert.AreEqual(string.Empty, RegexLiteralExtractor.ExtractRequiredLiteral("\\d+"));
    }

    [TestMethod]
    public void Extract_LongestRunWins() => Assert.AreEqual("report", RegexLiteralExtractor.ExtractRequiredLiteral("ab.*report"));

    [TestMethod]
    public void Extract_CharacterClassContentsAreNotLiteral() => Assert.AreEqual("x", RegexLiteralExtractor.ExtractRequiredLiteral("x[abc]y"));

    [TestMethod]
    public void Extract_TrailingLiteralAfterAnEndAnchor_IsStillRequired() => Assert.AreEqual("read", RegexLiteralExtractor.ExtractRequiredLiteral("^read.*\\.md$"));

    [TestMethod]
    public void Extract_OptionalAtom_IsNotClaimedAsRequired()
    {
        Assert.AreEqual("a", RegexLiteralExtractor.ExtractRequiredLiteral("ab?c"));
        Assert.AreEqual("a", RegexLiteralExtractor.ExtractRequiredLiteral("ab*c"));
        Assert.AreEqual("a", RegexLiteralExtractor.ExtractRequiredLiteral("ab{0,2}c"));
    }

    [TestMethod]
    public void Extract_OptionalGroup_IsNotClaimedAsRequired()
    {
        Assert.AreEqual("c", RegexLiteralExtractor.ExtractRequiredLiteral("(ab)?c"));
        Assert.AreEqual("c", RegexLiteralExtractor.ExtractRequiredLiteral("(ab){0,2}c"));
    }

    // An optional or quantified group must not contribute its contents, but text AFTER it is still
    // required of every match, so the run restarts past the group instead of dying with it.
    [TestMethod]
    public void Extract_OptionalAlternationGroup_KeepsOnlyTheTextAfterIt()
    {
        Assert.AreEqual("e", RegexLiteralExtractor.ExtractRequiredLiteral("(ab|cd)e"));
        Assert.AreEqual("e", RegexLiteralExtractor.ExtractRequiredLiteral("(ab|cd){0,2}e"));
    }

    [TestMethod]
    public void Extract_RepeatedAtom_StaysInTheRun()
    {
        Assert.AreEqual("ab", RegexLiteralExtractor.ExtractRequiredLiteral("ab+c"));
        Assert.AreEqual("ab", RegexLiteralExtractor.ExtractRequiredLiteral("ab{1,2}c"));
    }

    [TestMethod]
    public void Extract_LiteralAfterADroppedCharacter_StartsANewRun() => Assert.AreEqual("a", RegexLiteralExtractor.ExtractRequiredLiteral("ab?c"));

    [TestMethod]
    public void Extract_AnEscapedLiteralAfterADroppedCharacter_IsNotJoinedOntoTheRun()
    {
        // The contiguity rule applies to the escape branch too, and this is the shape that shows why: 'e'
        // is optional, so the escape's "." cannot be appended to "readm" -- no match must contain "readm."
        // ("readme.md" does not), which is what the extractor used to return. It is silent today because the
        // only consumer folds the value into a per-character mask, but the value itself broke the substring
        // contract stated at the top of RegexLiteralExtractor.
        Assert.AreEqual("readm", RegexLiteralExtractor.ExtractRequiredLiteral("^readme?\\.md$"));
        Assert.AreEqual("ab", RegexLiteralExtractor.ExtractRequiredLiteral("^abc?\\.d$"));
    }

    [TestMethod]
    public void Extract_EscapedQuantifier_IsStillLiteral() => Assert.AreEqual("a", RegexLiteralExtractor.ExtractRequiredLiteral("a\\+?b"));

    [TestMethod]
    public void Extract_TextAroundAnAlternationGroup_IsNotJoined()
    {
        Assert.AreEqual("a", RegexLiteralExtractor.ExtractRequiredLiteral("a(b|c)d"));
        Assert.AreEqual("baz", RegexLiteralExtractor.ExtractRequiredLiteral("(foo|bar)baz"));
    }

    [TestMethod]
    public void Extract_PlainGroup_ContentsAreStillScanned()
    {
        Assert.AreEqual("defghi", RegexLiteralExtractor.ExtractRequiredLiteral("abc(defghi)"));
        Assert.AreEqual("after", RegexLiteralExtractor.ExtractRequiredLiteral("abc(def)after"));
    }

    [TestMethod]
    public void Extract_PlainGroup_DoesNotJoinItsTwoSides() => Assert.AreEqual("abc", RegexLiteralExtractor.ExtractRequiredLiteral("abc(def)"));

    [TestMethod]
    public void Extract_TopLevelAlternation_ReportsNoLiteral() => Assert.AreEqual(string.Empty, RegexLiteralExtractor.ExtractRequiredLiteral("readme|notes"));

    [TestMethod]
    public void Extract_TextOutsideAnAlternationGroup_IsStillRequired()
    {
        Assert.AreEqual(".", RegexLiteralExtractor.ExtractRequiredLiteral(@"\.(?:ogg|mp3|wav)$"));
        Assert.AreEqual("report.", RegexLiteralExtractor.ExtractRequiredLiteral(@"report\.(?:txt|md)"));
    }

    [TestMethod]
    public void Extract_NestedAlternation_DoesNotClaimBranchText() => Assert.AreEqual("a", RegexLiteralExtractor.ExtractRequiredLiteral("a(b(c|d))e"));

    // A group that is not the match's own text contributes nothing. The NEGATIVE assertion is the case
    // that made this a wrong literal rather than a weak one: it asserts the text is ABSENT, so demanding
    // it of every candidate rejects exactly the names the pattern accepts. Regression: "/^(?!.*tmp).*\.md$/"
    // yielded "tmp", and a search for it returned nothing for "readme.md".
    [TestMethod]
    public void Extract_NegativeLookahead_DoesNotClaimTheAssertedText() =>
        Assert.AreEqual(".md", RegexLiteralExtractor.ExtractRequiredLiteral(@"^(?!.*tmp).*\.md$"));

    [TestMethod]
    public void Extract_NegativeLookbehind_DoesNotClaimTheAssertedText() =>
        Assert.AreEqual("post", RegexLiteralExtractor.ExtractRequiredLiteral(@"^(?<!pre)post$"));

    // The text AFTER a lookaround is still required, which is what the run has to restart on.
    [TestMethod]
    public void Extract_TextAfterAnAssertion_IsStillRequired() =>
        Assert.AreEqual("report", RegexLiteralExtractor.ExtractRequiredLiteral(@"^(?!.*tmp)report.*$"));

    // Metadata about how to match, carrying no text of the match at all. "(?i)" and "(?#...)" used to hand
    // their own syntax to the mask ("i", "#note"), which no matching name needs to contain.
    [TestMethod]
    public void Extract_InlineOptionGroup_ContributesNothing() =>
        Assert.AreEqual("abc", RegexLiteralExtractor.ExtractRequiredLiteral("(?i)abc"));

    [TestMethod]
    public void Extract_CommentGroup_ContributesNothing() =>
        Assert.AreEqual("abc", RegexLiteralExtractor.ExtractRequiredLiteral("(?#note)abc"));

    // The two forms that DO carry the match's text keep being scanned, so the rule above stays narrow.
    [TestMethod]
    public void Extract_NonCapturingAndNamedGroups_StillContributeTheirText()
    {
        Assert.AreEqual("abc", RegexLiteralExtractor.ExtractRequiredLiteral("(?<name>abc)"));
        Assert.AreEqual("abc", RegexLiteralExtractor.ExtractRequiredLiteral("(?:abc)def"));
    }

    // "\1" is the group it points at, never the character "1": "^(ab)\1$" matches "abab", which contains no
    // digit at all.
    [TestMethod]
    public void Extract_Backreference_ContributesNoLiteral() =>
        Assert.AreEqual("ab", RegexLiteralExtractor.ExtractRequiredLiteral(@"^(ab)\1$"));
}
