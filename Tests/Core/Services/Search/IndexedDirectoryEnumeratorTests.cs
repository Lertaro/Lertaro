using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

[TestClass]
public sealed class IndexedDirectoryEnumeratorTests
{
    [TestMethod]
    public void NormalizeDirectoryPath_WslPathUsesLexicalNormalization()
    {
        var path = @"\\wsl$\Ubuntu/home/testuser/~cache/";

        Assert.AreEqual(@"\\wsl$\Ubuntu\home\testuser\~cache\", IndexedDirectoryEnumerator.NormalizeDirectoryPath(path));
    }

    [TestMethod]
    public void NormalizeDirectoryPath_LocalRelativePathStillBecomesFullyQualified()
    {
        var normalized = IndexedDirectoryEnumerator.NormalizeDirectoryPath("relative");

        Assert.IsTrue(Path.IsPathFullyQualified(normalized));
    }

    [TestMethod]
    public void NormalizeIndexRoot_BareDriveLetter_GainsColonAndSeparator()
    {
        Assert.AreEqual(@"C:\", IndexedDirectoryEnumerator.NormalizeIndexRoot("C"));
        // Case is preserved here; the case-insensitive matching happens in IsUnderRoot downstream.
        Assert.AreEqual(@"c:\", IndexedDirectoryEnumerator.NormalizeIndexRoot("c"));
    }

    [TestMethod]
    public void NormalizeIndexRoot_TrailingSeparator_IsGuaranteed()
    {
        Assert.AreEqual(@"\\server\share\", IndexedDirectoryEnumerator.NormalizeIndexRoot(@"\\server\share"));
        Assert.AreEqual(@"D:\folder\", IndexedDirectoryEnumerator.NormalizeIndexRoot(@"D:/folder"));
    }

    [TestMethod]
    public void IsUnderRoot_RootItself_AndNestedPaths_Match_CaseInsensitive()
    {
        Assert.IsTrue(IndexedDirectoryEnumerator.IsUnderRoot(@"\\Server\Share", @"\\server\share\"));
        Assert.IsTrue(IndexedDirectoryEnumerator.IsUnderRoot(@"\\server\share\sub\deeper", @"\\server\share\"));
    }

    [TestMethod]
    public void IsUnderRoot_SiblingPrefix_DoesNotMatch()
    {
        Assert.IsFalse(IndexedDirectoryEnumerator.IsUnderRoot(@"\\server\share2\file", @"\\server\share\"));
        Assert.IsFalse(IndexedDirectoryEnumerator.IsUnderRoot(@"C:\Users\other", @"C:\Users\testuser\"));
    }
}
