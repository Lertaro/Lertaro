namespace Lertaro.Core.Tests;

[TestClass]
public sealed class WslPathTests
{
    [TestMethod]
    [DataRow(@"\\wsl$\Ubuntu\home\testuser", true)]
    [DataRow(@"\\WSL.LOCALHOST\Ubuntu\home", true)]
    [DataRow(@"\\wslbackup\share", false)]
    [DataRow(@"\\server\share", false)]
    [DataRow(@"C:\Work", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void IsPath_ClassifiesWithoutBroadUncFalsePositives(string? path, bool expected) =>
        Assert.AreEqual(expected, WslPath.IsPath(path));
}
