using Lertaro.Plugins.Files.Automation;

namespace Lertaro.Plugins.Files.Tests.Automation;

[TestClass]
public sealed class PathValidationTests
{
    [TestMethod]
    public void IsAccessibleDirectory_ExistingDirectory_ReturnsTrue() => Assert.IsTrue(PathValidation.IsAccessibleDirectory(Path.GetTempPath()));

    [TestMethod]
    public void IsAccessibleDirectory_WslPath_ReturnsTrue() => Assert.IsTrue(PathValidation.IsAccessibleDirectory(@"\\wsl$\Ubuntu\home"));

    [TestMethod]
    public void IsAccessibleDirectory_Null_ReturnsFalse() => Assert.IsFalse(PathValidation.IsAccessibleDirectory(null));

    [TestMethod]
    public void IsAccessibleDirectory_Empty_ReturnsFalse() => Assert.IsFalse(PathValidation.IsAccessibleDirectory(""));

    [TestMethod]
    public void IsAccessibleDirectory_Whitespace_ReturnsFalse() => Assert.IsFalse(PathValidation.IsAccessibleDirectory("   "));

    [TestMethod]
    public void IsAccessibleDirectory_RelativePath_ReturnsFalse() => Assert.IsFalse(PathValidation.IsAccessibleDirectory(@"sub\file.txt"));

    [TestMethod]
    public void IsAccessibleDirectory_NonexistentDirectory_ReturnsFalse() => Assert.IsFalse(PathValidation.IsAccessibleDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
}
