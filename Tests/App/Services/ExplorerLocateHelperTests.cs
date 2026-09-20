using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

// Which folder a "locate in Explorer" should end up showing. Only the decision is covered: the rest of
// the call is ShellWindows COM against a live Explorer window.
[TestClass]
public sealed class ExplorerLocateHelperTests
{
    [TestMethod]
    public void ResolveContainingFolder_Folder_IsItsOwnTarget() =>
        Assert.AreEqual(
            @"C:\folder\sub",
            ExplorerLocateHelper.ResolveContainingFolder(@"C:\folder\sub", path => path == @"C:\folder\sub"));

    [TestMethod]
    public void ResolveContainingFolder_File_ResolvesToItsParent() =>
        Assert.AreEqual(@"C:\folder", ExplorerLocateHelper.ResolveContainingFolder(@"C:\folder\file.txt", _ => false));

    [TestMethod]
    [DataRow(@"C:\")]
    [DataRow("shell:AppsFolder")]
    [DataRow("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    public void ResolveContainingFolder_NothingToShow_ReportsNoFolder(string path) =>
        // A drive root and a virtual shell token have no containing folder, which is what routes them to
        // the shell-locate fallback instead of pretending there was one to open. Null or empty, exactly:
        // only "no folder" is promised, not which spelling of it comes back.
        Assert.IsTrue(
            string.IsNullOrEmpty(ExplorerLocateHelper.ResolveContainingFolder(path, _ => false)),
            $"'{path}' resolved to a containing folder");
}
