using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.Core.SearchIndex.Query;

namespace Lertaro.Core.Tests.SearchIndex.Query;

[TestClass]
public sealed class RegexLiteralExtractorTests
{
    [TestMethod]
    public void Extract_AlternationWithClasses_KeepsOnlyTheLiteralRun()
    {
        // The "ab" before the wildcard is required by any match.
        Assert.AreEqual("ab", RegexLiteralExtractor.ExtractRequiredLiteral("^ab.c\\..{3}$"));
    }

    [TestMethod]
    public void Extract_ExtensionAlternation_StillFindsTheRequiredPrefix()
    {
        // "\.(?:ogg|mp3)$" -- "ogg" and "mp3" are branch-only and must not be claimed, but the escaped dot
        // before the group and the end anchor after it are required of every match. Bailing out entirely
        // (which this used to do) left the most common real-world pattern with no prefilter at all.
        Assert.AreEqual(".", RegexLiteralExtractor.ExtractRequiredLiteral("\\.(?:ogg|mp3)$"));
        Assert.AreEqual(".", RegexLiteralExtractor.ExtractRequiredLiteral("\\.(?:ogg|mp3|wav|flac)$"));
    }

    [TestMethod]
    public void Extract_DigitClassContributesNothing()
    {
        // "[0-9]{8}_" -- the quantifier body is skipped and the class contributes nothing, leaving "_".
        Assert.AreEqual("_", RegexLiteralExtractor.ExtractRequiredLiteral("^[0-9]{8}_"));
    }

    [TestMethod]
    public void Extract_NoLiteralAtAll_ReturnsEmpty()
    {
        // Nothing here is required verbatim, so this regex cannot be prefiltered.
        Assert.AreEqual(string.Empty, RegexLiteralExtractor.ExtractRequiredLiteral(".*"));
        Assert.AreEqual(string.Empty, RegexLiteralExtractor.ExtractRequiredLiteral("^.{5}$"));
        Assert.AreEqual(string.Empty, RegexLiteralExtractor.ExtractRequiredLiteral("\\d+"));
    }

    [TestMethod]
    public void Extract_LongestRunWins()
    {
        // Two runs separated by a wildcard: "ab" and "report". The longer one is the better prefilter.
        Assert.AreEqual("report", RegexLiteralExtractor.ExtractRequiredLiteral("ab.*report"));
    }

    [TestMethod]
    public void Extract_CharacterClassContentsAreNotLiteral()
    {
        // "x[abc]y": the class contributes nothing, leaving "x" and "y" as the runs, and "x" is first
        // and equally long, so "x" is reported.
        Assert.AreEqual("x", RegexLiteralExtractor.ExtractRequiredLiteral("x[abc]y"));
    }

    [TestMethod]
    public void Extract_TrailingLiteralAfterAnEndAnchor_IsStillRequired()
    {
        Assert.AreEqual("read", RegexLiteralExtractor.ExtractRequiredLiteral("^read.*\\.md$"));
    }

    // An optional quantifier makes the atom before it optional, so that character must not be claimed as
    // required: "ab?c" matches "ac". This used to return "ab" -- harmless while nothing consumed the
    // literal, and wrong results the moment it became a prefilter.
    [TestMethod]
    public void Extract_OptionalAtom_IsNotClaimedAsRequired()
    {
        Assert.AreEqual("a", RegexLiteralExtractor.ExtractRequiredLiteral("ab?c"));
        Assert.AreEqual("a", RegexLiteralExtractor.ExtractRequiredLiteral("ab*c"));
        Assert.AreEqual("a", RegexLiteralExtractor.ExtractRequiredLiteral("ab{0,2}c"));
    }

    // A minimum above zero keeps its atom required, so the run survives.
    [TestMethod]
    public void Extract_RepeatedAtom_StaysInTheRun()
    {
        Assert.AreEqual("ab", RegexLiteralExtractor.ExtractRequiredLiteral("ab+c"));
        Assert.AreEqual("ab", RegexLiteralExtractor.ExtractRequiredLiteral("ab{1,2}c"));
    }

    // The run is a CONTIGUOUS substring requirement. Once a character is dropped from the middle, what
    // follows cannot be joined to what came before: for "ab?c" the required characters are "a" and "c",
    // but "ac" is not a substring of "ac" (nor of "abc"), so returning it would be a wrong literal.
    [TestMethod]
    public void Extract_LiteralAfterADroppedCharacter_StartsANewRun()
    {
        var literal = RegexLiteralExtractor.ExtractRequiredLiteral("ab?c");

        Assert.AreEqual("a", literal);
    }

    [TestMethod]
    public void Extract_EscapedQuantifier_IsStillLiteral()
    {
        // "\\+" is a literal plus, so the "?" makes the PLUS optional, not the "a".
        Assert.AreEqual("a", RegexLiteralExtractor.ExtractRequiredLiteral("a\\+?b"));
    }

    // Text on either side of an alternation group is required, but it cannot be joined into one run: the
    // group sits between them, so no match places the two adjacent.
    [TestMethod]
    public void Extract_TextAroundAnAlternationGroup_IsNotJoined()
    {
        Assert.AreEqual("a", RegexLiteralExtractor.ExtractRequiredLiteral("a(b|c)d"));
        Assert.AreEqual("baz", RegexLiteralExtractor.ExtractRequiredLiteral("(foo|bar)baz"));
    }

    // A group is a break at both ends: text before it and text after it are not adjacent in a match, so the
    // longest run is whichever side is longer. A plain group's own contents are still scanned, because they
    // are required text like anything else.
    [TestMethod]
    public void Extract_PlainGroup_ContentsAreStillScanned()
    {
        Assert.AreEqual("defghi", RegexLiteralExtractor.ExtractRequiredLiteral("abc(defghi)"));
        Assert.AreEqual("after", RegexLiteralExtractor.ExtractRequiredLiteral("abc(def)after"));
    }

    [TestMethod]
    public void Extract_PlainGroup_DoesNotJoinItsTwoSides()
    {
        // "abcdef" would be wrong: no match of "abc(def)" contains "abcdef".
        Assert.AreEqual("abc", RegexLiteralExtractor.ExtractRequiredLiteral("abc(def)"));
    }

    [TestMethod]
    public void Extract_TopLevelAlternation_ReportsNoLiteral()
    {
        // At depth 0 there is nothing shared between the branches at all.
        Assert.AreEqual(string.Empty, RegexLiteralExtractor.ExtractRequiredLiteral("readme|notes"));
    }

    // A branch group is skipped whole, but the text OUTSIDE it is still required of every match. Bailing
    // out for the whole pattern (which this used to do) left the commonest real-world shape -- a list of
    // extensions -- with no prefilter at all.
    [TestMethod]
    public void Extract_TextOutsideAnAlternationGroup_IsStillRequired()
    {
        Assert.AreEqual(".", RegexLiteralExtractor.ExtractRequiredLiteral(@"\.(?:ogg|mp3|wav)$"));
        // The escaped dot is a literal dot, so it belongs to the run before the group.
        Assert.AreEqual("report.", RegexLiteralExtractor.ExtractRequiredLiteral(@"report\.(?:txt|md)"));
    }

    // Nested branches must not be read as the group's own level: the outer group has no '|' of its own
    // here, but its inner content is still alternatives, so nothing inside may be claimed.
    [TestMethod]
    public void Extract_NestedAlternation_DoesNotClaimBranchText()
    {
        Assert.AreEqual("a", RegexLiteralExtractor.ExtractRequiredLiteral("a(b(c|d))e"));
    }
}

[TestClass]
public sealed class RegexQueryParserTests
{
    [TestMethod]
    public void Split_RegexAlone_LeavesNoText()
    {
        var rest = RegexQueryParser.Split("regex:/^ab.c\\..{3}$/", out var clauses);

        Assert.AreEqual(string.Empty, rest);
        Assert.HasCount(1, clauses);
        Assert.AreEqual("^ab.c\\..{3}$", clauses[0].Pattern);
        Assert.AreEqual("ab", clauses[0].RequiredLiteral);
    }

    [TestMethod]
    public void Split_RegexWithTrailingWord_KeepsTheWordAsText()
    {
        var rest = RegexQueryParser.Split("regex:/^ab.c\\..{3}$/ zip", out var clauses);

        Assert.AreEqual("zip", rest);
        Assert.HasCount(1, clauses);
    }

    [TestMethod]
    public void Split_EscapedSlashInsidePattern_DoesNotEndTheClause()
    {
        var rest = RegexQueryParser.Split("regex:/a\\/b/ tail", out var clauses);

        Assert.AreEqual("tail", rest);
        Assert.HasCount(1, clauses);
        Assert.AreEqual("a\\/b", clauses[0].Pattern);
    }

    [TestMethod]
    public void Split_NoRegexClause_ReturnsTheQueryUntouched()
    {
        var rest = RegexQueryParser.Split("read me", out var clauses);

        Assert.AreEqual("read me", rest);
        Assert.IsEmpty(clauses);
    }

    [TestMethod]
    public void Split_UnterminatedRegex_StaysLiteralText()
    {
        var rest = RegexQueryParser.Split("regex:/ab", out var clauses);

        Assert.IsEmpty(clauses);
        Assert.AreEqual("regex:/ab", rest);
    }

    [TestMethod]
    public void Split_PrefixNotAtTokenBoundary_StaysLiteralText()
    {
        var rest = RegexQueryParser.Split("aregex:/ab/", out var clauses);

        Assert.IsEmpty(clauses);
        Assert.AreEqual("aregex:/ab/", rest);
    }

    [TestMethod]
    public void Split_TwoClauses_BothExtractedAndAnded()
    {
        var rest = RegexQueryParser.Split("regex:/^a/ regex:/\\.md$/ report", out var clauses);

        Assert.AreEqual("report", rest);
        Assert.HasCount(2, clauses);
    }

    [TestMethod]
    public void Split_NoExtractableLiteral_YieldsAnEmptyLiteral()
    {
        // Asserted on the literal itself, which is what the prefilter consumes (via
        // FzfPattern.RequiredRegexLiteral); there is no separate "can prefilter" flag to keep in sync.
        _ = RegexQueryParser.Split("regex:/.*/", out var clauses);

        Assert.HasCount(1, clauses);
        Assert.AreEqual(string.Empty, clauses[0].RequiredLiteral);
    }

    // The headline use case end to end: the clause must reach the pattern matcher AND must not have sent
    // the query down the path-mode branch on its way there. Regression: the escaped dot's backslash made
    // SearchQueryParser call this a full path, so "lertaro regex:/\.exe$/" matched nothing.
    [TestMethod]
    public void Split_EscapedDotQuery_ReachesTheMatcherFromANonPathQuery()
    {
        var query = @"lertaro regex:/\.exe$/";

        var rest = RegexQueryParser.Split(query, out var clauses);
        var parsed = SearchQueryParser.Parse(query);
        var pattern = FzfPattern.Parse(query);

        Assert.AreEqual("lertaro", rest);
        Assert.HasCount(1, clauses);
        Assert.AreEqual(@"\.exe$", clauses[0].Pattern);
        Assert.IsFalse(parsed.IsPathMode);

        Assert.IsTrue(pattern.TryMatch("Lertaro.App.exe", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("Lertaro.App.dll", out _, FzfScoringScheme.Default));
    }

    // The byte matcher is the ASCII fast path and cannot apply a regex, so it must SAY so rather than
    // answer. It cannot simply run the terms it does understand: for a regex-only query that means "no
    // positive terms", which rejects every name (the search returns nothing at all), and for a query with
    // an ordinary term as well it returns a match that ignored the regex entirely. Callers use this flag
    // to send those candidates down the char path instead.
    [TestMethod]
    public void BytePattern_ReportsThatItCannotApplyRegexClauses()
    {
        Assert.IsTrue(FzfBytePattern.From(FzfPattern.Parse(@"regex:/\.exe$/")).HasRegexClauses);
        Assert.IsTrue(FzfBytePattern.From(FzfPattern.Parse(@"lertaro regex:/\.exe$/")).HasRegexClauses);
        Assert.IsFalse(FzfBytePattern.From(FzfPattern.Parse("lertaro")).HasRegexClauses);
        Assert.IsFalse(FzfBytePattern.From(FzfPattern.Parse(":temp")).HasRegexClauses);
    }

    [TestMethod]
    public void BytePattern_RegexOnlyQuery_DoesNotClaimAMatch()
    {
        // Whatever a caller does with the flag, the byte matcher itself must not report a hit for a query
        // it cannot evaluate -- an empty result masquerading as a successful match is the worse failure.
        var bytePattern = FzfBytePattern.From(FzfPattern.Parse(@"regex:/\.exe$/"));

        Assert.IsFalse(bytePattern.TryMatch(
            System.Text.Encoding.ASCII.GetBytes("a.exe"),
            out _,
            FzfScoringScheme.Default,
            new FzfSlab(),
            new FzfByteBuffers()));
    }

    // What the prefilter consumes: the longest literal any regex clause requires a matching name to
    // contain, so a regex search can reject candidates on the cheap character mask instead of running the
    // regex engine against every indexed name.
    [TestMethod]
    public void RequiredRegexLiteral_LongestExtractableRunWins()
    {
        Assert.AreEqual(".exe", FzfPattern.Parse(@"regex:/\.exe$/").RequiredRegexLiteral);
        Assert.AreEqual("ab", FzfPattern.Parse(@"regex:/^ab.c\..{3}$/").RequiredRegexLiteral);
    }

    [TestMethod]
    public void RequiredRegexLiteral_NoExtractableRun_IsEmpty()
    {
        // A bare wildcard and a class-only pattern have nothing a mask could demand. Note that an
        // alternation is NOT in this list: its surrounding text is still required (see the extractor tests),
        // so "\\.(?:ogg|mp3)$" prefilteres on the "." before the group.
        Assert.AreEqual(string.Empty, FzfPattern.Parse(@"regex:/.*/").RequiredRegexLiteral);
        Assert.AreEqual(string.Empty, FzfPattern.Parse(@"regex:/^.{5}$/").RequiredRegexLiteral);
        Assert.AreEqual(string.Empty, FzfPattern.Parse(@"regex:/\d+/").RequiredRegexLiteral);
        Assert.AreEqual(string.Empty, FzfPattern.Parse("plain").RequiredRegexLiteral);
    }

    [TestMethod]
    public void RequiredRegexLiteral_TakesTheLongestAcrossClauses()
    {
        // Both clauses must match, so the longest run is the strongest prefilter either can offer.
        var pattern = FzfPattern.Parse(@"regex:/ab.*/ regex:/readme/");

        Assert.AreEqual("readme", pattern.RequiredRegexLiteral);
    }
}
