namespace Lertaro.Core.Tests.Indexer;

[TestClass]
public sealed class GlobToRegexTests
{
    [TestMethod]
    [DataRow("readme.txt", "readme.txt", true)]
    [DataRow("readme.txt", "README.TXT", true)] // ignoreCase defaults to true
    [DataRow("readme.txt", "notes.txt", false)]
    public void Compile_LiteralPattern_MatchesExactNameOnly(string glob, string candidate, bool expected)
    {
        var regex = GlobToRegex.Compile(glob);

        Assert.AreEqual(expected, regex.IsMatch(candidate));
    }

    [TestMethod]
    public void Compile_SingleStarNoSlash_MatchesAtAnyDepthWithinOneSegment()
    {
        // No slash in the glob means it's not root-anchored -- it matches the filename at any depth,
        // same as a plain literal glob (see Compile_NoSlash_MatchesAtAnyDepth), just with a wildcard
        // segment. The single "*" still can't cross a path separator *itself*.
        var regex = GlobToRegex.Compile("*.txt");

        Assert.IsTrue(regex.IsMatch("readme.txt"));
        Assert.IsTrue(regex.IsMatch(@"docs\readme.txt"));
    }

    [TestMethod]
    public void Compile_SingleStarAnchoredBySlash_DoesNotCrossPathSeparator()
    {
        var regex = GlobToRegex.Compile("src/*.txt");

        Assert.IsTrue(regex.IsMatch(@"c:\src\readme.txt"));
        Assert.IsFalse(regex.IsMatch(@"c:\src\sub\readme.txt")); // single * must not cross a path separator
    }

    [TestMethod]
    public void Compile_DoubleStar_MatchesAcrossPathSegments()
    {
        var regex = GlobToRegex.Compile("/src/**/*.cs");

        Assert.IsTrue(regex.IsMatch(@"c:\src\a\b\c\File.cs"));
        Assert.IsTrue(regex.IsMatch(@"c:\src\File.cs"));
        Assert.IsFalse(regex.IsMatch(@"c:\lib\File.cs"));
    }

    [TestMethod]
    public void Compile_QuestionMark_MatchesExactlyOneNonSeparatorCharacter()
    {
        var regex = GlobToRegex.Compile("file?.txt");

        Assert.IsTrue(regex.IsMatch("file1.txt"));
        Assert.IsFalse(regex.IsMatch("file12.txt"));
        Assert.IsFalse(regex.IsMatch("file.txt"));
    }

    [TestMethod]
    public void Compile_CharacterClass_MatchesOnlyListedCharacters()
    {
        var regex = GlobToRegex.Compile("file[12].txt");

        Assert.IsTrue(regex.IsMatch("file1.txt"));
        Assert.IsTrue(regex.IsMatch("file2.txt"));
        Assert.IsFalse(regex.IsMatch("file3.txt"));
    }

    [TestMethod]
    public void Compile_BraceAlternation_MatchesAnyAlternative()
    {
        var regex = GlobToRegex.Compile("*.{jpg,png}");

        Assert.IsTrue(regex.IsMatch("photo.jpg"));
        Assert.IsTrue(regex.IsMatch("photo.png"));
        Assert.IsFalse(regex.IsMatch("photo.gif"));
    }

    [TestMethod]
    public void Compile_RegexSpecialCharactersInLiteral_AreEscaped()
    {
        // "." in a glob is a literal dot, not "any character" -- must not accidentally match "aXtxt".
        var regex = GlobToRegex.Compile("a.txt");

        Assert.IsTrue(regex.IsMatch("a.txt"));
        Assert.IsFalse(regex.IsMatch("aXtxt"));
    }

    [TestMethod]
    public void Compile_NoSlash_MatchesAtAnyDepth()
    {
        var regex = GlobToRegex.Compile("node_modules");

        Assert.IsTrue(regex.IsMatch(@"c:\project\node_modules"));
        Assert.IsTrue(regex.IsMatch(@"c:\project\sub\node_modules"));
    }

    [TestMethod]
    public void Compile_MismatchedBracket_ThrowsArgumentException() => Assert.ThrowsExactly<ArgumentException>(() => GlobToRegex.Compile("file]"));

    [TestMethod]
    public void Compile_MismatchedBrace_ThrowsArgumentException() => Assert.ThrowsExactly<ArgumentException>(() => GlobToRegex.Compile("file}"));

    [TestMethod]
    public void Compile_UnclosedBracket_ThrowsArgumentException() => Assert.ThrowsExactly<ArgumentException>(() => GlobToRegex.Compile("file[abc"));

    [TestMethod]
    public void Convert_EmptyGlob_ReturnsEmptyString() => Assert.AreEqual(string.Empty, GlobToRegex.Convert(""));
}
