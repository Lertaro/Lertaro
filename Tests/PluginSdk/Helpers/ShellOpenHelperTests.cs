using Lertaro.PluginSdk.Helpers;

namespace Lertaro.PluginSdk.Tests.Helpers;

// Only the guards are covered. Everything past them is a shell call that needs a live desktop, and a test
// that exercised it would open real windows on whoever runs the suite.
[TestClass]
public sealed class ShellOpenHelperTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void TryOpenFolder_NoFolder_IsRefusedWithoutCallingTheShell(string? folderPath) =>
        Assert.IsFalse(ShellOpenHelper.TryOpenFolder(folderPath));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void TryRevealInFolder_NoItem_IsRefusedWithoutCallingTheShell(string? itemPath) =>
        Assert.IsFalse(ShellOpenHelper.TryRevealInFolder(itemPath));
}
