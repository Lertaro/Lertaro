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
}
