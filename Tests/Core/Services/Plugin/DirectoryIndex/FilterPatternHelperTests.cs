using Lertaro.Core.Services.Plugin.DirectoryIndex;

namespace Lertaro.Core.Tests.Services.Plugin.DirectoryIndex;

[TestClass]
public sealed class FilterPatternHelperTests
{
    [TestMethod]
    public void Split_EmptyOrWhitespace_DefaultsToMatchAll()
    {
        CollectionAssert.AreEqual(new[] { "*" }, FilterPatternHelper.Split(""));
        CollectionAssert.AreEqual(new[] { "*" }, FilterPatternHelper.Split("   "));
    }

    [TestMethod]
    public void Split_SinglePattern_ReturnsThatPattern() => CollectionAssert.AreEqual(new[] { "*.lnk" }, FilterPatternHelper.Split("*.lnk"));

    [TestMethod]
    public void Split_MixedSeparatorsAndWhitespace_TrimsEachEntry() => CollectionAssert.AreEqual(new[] { "*.exe", "*.lnk", "*.bat" }, FilterPatternHelper.Split(" *.exe; *.lnk , *.bat "));

    // null is the "don't filter at all" signal a subtree walk checks once instead of running a
    // wildcard match per entry that could only ever return true.
    [TestMethod]
    public void SplitOrNullIfMatchAll_MatchAllPatterns_ReturnNull()
    {
        Assert.IsNull(FilterPatternHelper.SplitOrNullIfMatchAll("*"));
        Assert.IsNull(FilterPatternHelper.SplitOrNullIfMatchAll("*.*"));
        Assert.IsNull(FilterPatternHelper.SplitOrNullIfMatchAll(""));
        Assert.IsNull(FilterPatternHelper.SplitOrNullIfMatchAll(null));
        // One match-all entry makes the whole list match-all, whatever else it lists.
        Assert.IsNull(FilterPatternHelper.SplitOrNullIfMatchAll("*.exe;*"));
    }

    [TestMethod]
    public void SplitOrNullIfMatchAll_RealPatterns_ReturnThem() => CollectionAssert.AreEqual(new[] { "*.exe", "*.lnk" }, FilterPatternHelper.SplitOrNullIfMatchAll("*.exe;*.lnk"));

    [TestMethod]
    public void Combine_MergesDistinctPatternsAndKeepsMatchAllDominant()
    {
        Assert.AreEqual("*.exe;*.lnk", FilterPatternHelper.Combine("*.exe", "*.lnk;*.EXE"));
        Assert.AreEqual("*", FilterPatternHelper.Combine("*.exe", "*"));
    }

    [TestMethod]
    public void Matches_ExtensionPattern_IsCaseInsensitiveAndAnchoredToTheExtension()
    {
        var patterns = new[] { "*.exe" };

        Assert.IsTrue(FilterPatternHelper.Matches("Setup.EXE", patterns));
        Assert.IsFalse(FilterPatternHelper.Matches("setup.exe.txt", patterns));
        Assert.IsFalse(FilterPatternHelper.Matches("exe", patterns));
    }

    [TestMethod]
    public void Matches_AnyPatternInTheList_IsEnough()
    {
        var patterns = new[] { "*.exe", "note?.txt" };

        Assert.IsTrue(FilterPatternHelper.Matches("app.exe", patterns));
        Assert.IsTrue(FilterPatternHelper.Matches("note1.txt", patterns));
        Assert.IsFalse(FilterPatternHelper.Matches("notes12.txt", patterns));
    }

    // "*.*" is the DOS spelling of "everything", which is how Directory.EnumerateFiles reads it --
    // taken literally as a Win32 expression it would drop every extension-less name instead.
    [TestMethod]
    public void Matches_DosMatchAllPattern_MatchesNamesWithoutADot() => Assert.IsTrue(FilterPatternHelper.Matches("Makefile", new[] { "*.*" }));

    // The other half of the same translation: a trailing dot is the DOS spelling of "no extension".
    // Untranslated it would be read as a literal period, which no filename ends with, so the pattern
    // would match nothing at all instead of exactly the extension-less names.
    [TestMethod]
    public void Matches_TrailingDotPattern_MatchesOnlyNamesWithoutAnExtension()
    {
        var patterns = new[] { "*." };

        Assert.IsTrue(FilterPatternHelper.Matches("Makefile", patterns));
        Assert.IsFalse(FilterPatternHelper.Matches("notes.txt", patterns));
    }

    // '?' is one character in the middle of a name, but DOS semantics let one sitting right before the
    // extension match ZERO characters as well -- "log?.txt" really does match "log.txt", exactly as
    // `dir log?.txt` and Directory.EnumerateFiles do. Surprising, and worth pinning rather than
    // "fixing": the whole point of translating the expression is to agree with a live walk.
    [TestMethod]
    public void Matches_SingleCharacterWildcard_FollowsDosSemantics()
    {
        var beforeExtension = new[] { "log?.txt" };
        Assert.IsTrue(FilterPatternHelper.Matches("log1.txt", beforeExtension));
        Assert.IsTrue(FilterPatternHelper.Matches("log.txt", beforeExtension));
        Assert.IsFalse(FilterPatternHelper.Matches("log12.txt", beforeExtension));

        var midName = new[] { "l?g.txt" };
        Assert.IsTrue(FilterPatternHelper.Matches("log.txt", midName));
        Assert.IsFalse(FilterPatternHelper.Matches("lg.txt", midName));
    }

    // The pattern matches a NAME, never a path: it has no directory component to match against, so a
    // separator in it can only ever fail (recursion is what reaches subdirectories, see
    // DirectoryEnumerator).
    [TestMethod]
    public void Matches_PatternWithADirectoryComponent_NeverMatches()
    {
        Assert.IsFalse(FilterPatternHelper.Matches("a.txt", new[] { @"sub\*.txt" }));
        Assert.IsFalse(FilterPatternHelper.Matches("a.txt", new[] { "**/*.txt" }));
    }
}
