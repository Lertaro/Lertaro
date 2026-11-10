using Lertaro.Core.SearchIndex.Query;
using Lertaro.Core.SearchIndex.Fzf;

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
    // that decision is made. Regression: "lertaro /\.exe$/" was read as a full path named
    // "lertaro /\.exe$" and returned nothing at all -- the query was not a path search in any sense.
    [TestMethod]
    public void Parse_RegexWithAnEscapedDot_IsNotPathMode()
    {
        var result = SearchQueryParser.Parse(@"lertaro /\.exe$/");

        Assert.IsFalse(result.IsPathMode);
        Assert.IsNull(result.PathPatternLower);
    }

    [TestMethod]
    public void Parse_RegexWithAnEscapedSlash_IsNotPathMode()
    {
        // The slash is escaped, so it belongs to the clause rather than to a path. An UNescaped one inside
        // the body is what makes a word stop being a clause at all (see RegexQueryParser), which is what
        // keeps "/usr/local/" a path -- either way the escaped form is the one that must not flip the
        // query into path mode.
        var result = SearchQueryParser.Parse(@"/^a\/b/");

        Assert.IsFalse(result.IsPathMode);
    }

    [TestMethod]
    public void Parse_RegexClause_DoesNotLeaveItsTextInThePathPattern()
    {
        // The clause is not part of what gets searched, so it must not leak into the path either.
        var result = SearchQueryParser.Parse(@"/\.exe$/ report");

        Assert.IsFalse(result.IsPathMode);
        Assert.IsNull(result.ExactPathLower);
    }

    // The escape hatch must stay exactly as wide as it needs to be: a genuine path still switches mode.
    [TestMethod]
    public void Parse_RealPathWithRegexClause_IsStillPathMode()
    {
        var result = SearchQueryParser.Parse(@"d:\projects /\.cs$/");

        Assert.IsTrue(result.IsPathMode);
        Assert.IsNotNull(result.PathPatternLower);
        Assert.AreEqual(@"d:\projects", result.PathPatternLower, "the clause must not leak into the path");
    }

    // One drive rule, read once: FzfPattern.Parse and the highlighter's drive filter both go through
    // IsBareDriveSpec here, so a query cannot scope to a drive for one reader and stay literal text for the
    // other. Pinned on the matching side too (FzfPatternTests.Parse_DriveLetterWithNoSpace_IsNotADrive).
    [TestMethod]
    public void Parse_DriveLetterWithNoSpace_IsNotADrive()
    {
        var result = SearchQueryParser.Parse("c:readme");

        Assert.IsFalse(result.IsPathMode, "no separator, so this is not a path");
        Assert.IsNull(result.TargetDrive);
    }

    [TestMethod]
    public void Parse_NonAsciiLetterBeforeColon_IsNotADriveAnywhere()
    {
        // A letter that cannot name a Windows drive must not scope the search either, and both readers
        // have to say so: guessing one used to leave the query filtered on a drive that does not exist.
        Assert.IsNull(SearchQueryParser.Parse("中: x").TargetDrive);
        Assert.IsNull(FzfPattern.Parse("中: x").TargetDrive);
    }

    [TestMethod]
    public void Parse_PathWithRegex_PreservesRegexForPathMatcher()
    {
        var result = SearchQueryParser.Parse(@"c:\projects\ /^readme\.txt$/");

        Assert.IsTrue(result.IsPathMode);
        Assert.IsNotNull(result.Regexes);
        Assert.HasCount(1, result.Regexes);
        Assert.AreEqual(@"^readme\.txt$", result.Regexes[0]);
    }

    // The clause syntax is "/.../", and '/' is also the alternate path separator, so these pin that the
    // path reading still wins wherever the two could be confused.
    [TestMethod]
    [DataRow("C:/Users/me", DisplayName = "forward-slash drive path")]
    [DataRow("/mnt/c/Users", DisplayName = "forward-slash absolute path")]
    [DataRow("/usr/local/", DisplayName = "multi-segment path with a trailing separator")]
    public void Parse_ForwardSlashPath_IsStillPathMode(string query)
    {
        var result = SearchQueryParser.Parse(query);

        Assert.IsTrue(result.IsPathMode, $"{query} is a path, not a regex clause");
        Assert.IsNull(result.Regexes);
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
