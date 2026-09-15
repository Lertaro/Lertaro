using Lertaro.Core.SearchIndex.Query;

namespace Lertaro.Core.Tests.SearchIndex.Query;

[TestClass]
public sealed class SearchQueryParserTests
{
    [TestMethod]
    public void Parse_PlainKeyword_IsNotPathMode()
    {
        var result = SearchQueryParser.Parse("readme");

        Assert.IsFalse(result.IsPathMode);
        Assert.IsNull(result.TargetDrive);
    }

    [TestMethod]
    public void Parse_DriveLetterTerm_SetsTargetDriveWithoutPathMode()
    {
        var result = SearchQueryParser.Parse("c: readme");

        Assert.IsFalse(result.IsPathMode);
        Assert.AreEqual("c", result.TargetDrive);
    }

    // A regex clause is full of the characters that decide path mode, so it has to be lifted out before
    // that decision is made. Regression: "lertaro regex:/\.exe$/" was read as a full path named
    // "lertaro regex:\.exe$" and returned nothing at all -- the query was not a path search in any sense.
    [TestMethod]
    public void Parse_RegexWithAnEscapedDot_IsNotPathMode()
    {
        var result = SearchQueryParser.Parse(@"lertaro regex:/\.exe$/");

        Assert.IsFalse(result.IsPathMode);
        Assert.IsNull(result.PathPatternLower);
    }

    [TestMethod]
    public void Parse_RegexWithAnEscapedSlash_IsNotPathMode()
    {
        // The slash is escaped, so it belongs to the clause rather than to a path. (An UNescaped slash
        // closes the clause, so whatever follows it is ordinary text and is judged as such -- either way
        // the escaped form is the one that must not flip the query into path mode.)
        var result = SearchQueryParser.Parse(@"regex:/^a\/b/");

        Assert.IsFalse(result.IsPathMode);
    }

    [TestMethod]
    public void Parse_RegexClause_DoesNotLeaveItsTextInThePathPattern()
    {
        // The clause is not part of what gets searched, so it must not leak into the path either.
        var result = SearchQueryParser.Parse(@"regex:/\.exe$/ report");

        Assert.IsFalse(result.IsPathMode);
        Assert.IsNull(result.ExactPathLower);
    }

    // The escape hatch must stay exactly as wide as it needs to be: a genuine path still switches mode.
    [TestMethod]
    public void Parse_RealPathWithRegexClause_IsStillPathMode()
    {
        var result = SearchQueryParser.Parse(@"d:\projects regex:/\.cs$/");

        Assert.IsTrue(result.IsPathMode);
        Assert.IsNotNull(result.PathPatternLower);
        Assert.IsFalse(result.PathPatternLower.Contains("regex:", StringComparison.Ordinal));
    }

    // The drive test here has to stay identical to FzfPattern.Parse's, or the two disagree about what
    // the same query says. This one only reads the drive and never consumes anything, which is why the
    // swallowed-term bug lived on the other side alone; pinned on both
    // (FzfPatternTests.Parse_DriveLetterWithNoSpace_KeepsTheRestAsATerm) so a change to either rule
    // without the other shows up as a failure rather than as a search that quietly misbehaves.
    [TestMethod]
    public void Parse_DriveLetterWithNoSpace_StillSetsTargetDrive()
    {
        var result = SearchQueryParser.Parse("c:readme");

        Assert.IsFalse(result.IsPathMode, "no separator, so this is not a path");
        Assert.AreEqual("c", result.TargetDrive);
    }

    [TestMethod]
    public void Parse_DrivePath_IsPathModeWithNormalizedDrive()
    {
        var result = SearchQueryParser.Parse(@"c:\foo\bar");

        Assert.IsTrue(result.IsPathMode);
        Assert.AreEqual("c", result.TargetDrive);
        Assert.AreEqual(@"c:\foo\bar", result.PathPatternLower);
        Assert.IsFalse(result.PathEndsWithSeparator);
    }

    [TestMethod]
    public void Parse_DrivePathWithTrailingSeparator_SetsPathEndsWithSeparator()
    {
        var result = SearchQueryParser.Parse(@"c:\foo\bar\");

        Assert.IsTrue(result.PathEndsWithSeparator);
        Assert.AreEqual(@"c:\foo\bar", result.ExactPathLower);
    }

    [TestMethod]
    public void Parse_ForwardSlashes_AreNormalizedToBackslashes()
    {
        var result = SearchQueryParser.Parse("c:/foo/bar");

        Assert.IsTrue(result.IsPathMode);
        Assert.AreEqual(@"c:\foo\bar", result.PathPatternLower);
    }

    [TestMethod]
    public void Parse_DriveRootShorthand_NormalizesToVolumeSeparatorForm()
    {
        // "c\foo" (drive letter immediately followed by a separator, no colon) is treated the same
        // as "c:\foo" -- see SearchQueryParser.TryNormalizeDrivePath.
        var result = SearchQueryParser.Parse(@"c\foo");

        Assert.IsTrue(result.IsPathMode);
        Assert.AreEqual("c", result.TargetDrive);
        Assert.AreEqual(@"c:\foo", result.PathPatternLower);
    }

    [TestMethod]
    public void Parse_MixedCaseQuery_IsLowercased()
    {
        var result = SearchQueryParser.Parse(@"C:\Foo\BAR");

        Assert.AreEqual(@"c:\foo\bar", result.PathPatternLower);
    }

    [TestMethod]
    [DataRow(@"c:\foo\bar", @"c:\foo\bar")]
    [DataRow(@"c:\foo\bar\", @"c:\foo\bar")]
    [DataRow(@"c:\foo\bar\\\", @"c:\foo\bar")]
    [DataRow(@"c:\", @"c:\")]
    public void NormalizeExactPath_TrimsTrailingSeparators(string input, string expected) => Assert.AreEqual(expected, SearchQueryParser.NormalizeExactPath(input));
}
