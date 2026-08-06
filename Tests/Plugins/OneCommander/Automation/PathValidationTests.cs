using Lertaro.Plugins.OneCommander.Automation;

namespace Lertaro.Plugins.OneCommander.Tests.Automation;

[TestClass]
public sealed class PathValidationTests
{
    [TestMethod]
    public void LooksLikeRootedPath_LocalDrivePath_ReturnsTrue() => Assert.IsTrue(PathValidation.LooksLikeRootedPath(@"C:\Projects\file.txt"));

    [TestMethod]
    public void LooksLikeRootedPath_UncPath_ReturnsTrue() => Assert.IsTrue(PathValidation.LooksLikeRootedPath(@"\\server\share\file.txt"));

    [TestMethod]
    public void LooksLikeRootedPath_Null_ReturnsFalse() => Assert.IsFalse(PathValidation.LooksLikeRootedPath(null));

    [TestMethod]
    public void LooksLikeRootedPath_Empty_ReturnsFalse() => Assert.IsFalse(PathValidation.LooksLikeRootedPath(""));

    [TestMethod]
    public void LooksLikeRootedPath_Whitespace_ReturnsFalse() => Assert.IsFalse(PathValidation.LooksLikeRootedPath("   "));

    [TestMethod]
    public void LooksLikeRootedPath_RelativePath_ReturnsFalse() => Assert.IsFalse(PathValidation.LooksLikeRootedPath(@"sub\file.txt"));

    [TestMethod]
    public void LooksLikeRootedPath_JustADriveLetter_ReturnsFalse() => Assert.IsFalse(PathValidation.LooksLikeRootedPath("C"));
}
