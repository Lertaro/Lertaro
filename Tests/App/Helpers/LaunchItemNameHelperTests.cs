using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

[TestClass]
public sealed class LaunchItemNameHelperTests
{
    [TestMethod]
    [DataRow("tool.lnk", "tool")]
    [DataRow("script.bat", "script")]
    [DataRow("script.cmd", "script")]
    [DataRow("tool.exe", "tool")]
    public void HideKnownExtension_RemovesOnlySupportedExtensions(string name, string expected)
        => Assert.AreEqual(expected, LaunchItemNameHelper.HideKnownExtension(name));

}
