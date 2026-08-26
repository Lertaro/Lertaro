using System.IO;
using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

[TestClass]
public sealed class FavoritePathResolverTests
{
    [TestMethod]
    public void Expand_EnvironmentVariable_ReturnsExpandedPath()
    {
        var expected = Environment.GetEnvironmentVariable("TEMP");

        Assert.IsNotNull(expected);
        Assert.AreEqual(expected, FavoritePathResolver.Expand("%TEMP%"));
    }

    [TestMethod]
    public void Expand_Blank_ReturnsOriginal()
    {
        const string blank = "   ";

        Assert.AreEqual(blank, FavoritePathResolver.Expand(blank));
    }

    [TestMethod]
    public void IsVirtualPath_ShellPrefix_True()
        => Assert.IsTrue(FavoritePathResolver.IsVirtualPath("shell:Downloads"));

    [TestMethod]
    public void IsVirtualPath_ClsidToken_True()
        => Assert.IsTrue(FavoritePathResolver.IsVirtualPath("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"));

    [TestMethod]
    public void IsVirtualPath_RealPath_False()
        => Assert.IsFalse(FavoritePathResolver.IsVirtualPath(@"C:\Users\testuser\Desktop"));

    [TestMethod]
    public void Resolve_ShellVirtualPath_UsesInjectedResolver()
    {
        var resolved = FavoritePathResolver.Resolve(
            "shell:Downloads",
            _ => @"C:\Users\testuser\Desktop");

        Assert.AreEqual(@"C:\Users\testuser\Desktop", resolved);
    }

    [TestMethod]
    public void Resolve_EnvironmentVariable_ExpandsBeforeVirtualResolver()
    {
        var temp = Environment.GetEnvironmentVariable("TEMP");
        Assert.IsNotNull(temp);

        var resolved = FavoritePathResolver.Resolve(@"%TEMP%\x", virtualPathResolver: p => p);

        Assert.AreEqual(Path.Combine(temp!, "x"), resolved);
    }

    [TestMethod]
    public void NormalizeForComparison_ShellVirtualPath_ReturnsRawWithoutGetFullPath()
        => Assert.AreEqual("shell:Downloads", FavoritePathResolver.NormalizeForComparison("shell:Downloads"));

    [TestMethod]
    public void NormalizeForComparison_EnvironmentVariable_ReturnsFullPath()
    {
        var expected = Path.GetFullPath(Environment.ExpandEnvironmentVariables("%TEMP%"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        Assert.AreEqual(expected, FavoritePathResolver.NormalizeForComparison("%TEMP%"));
    }

    [TestMethod]
    public void NormalizeForComparison_WebUrl_ReturnsRaw()
        => Assert.AreEqual("https://example.com", FavoritePathResolver.NormalizeForComparison("https://example.com"));
}
