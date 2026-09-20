using Lertaro.PluginSdk.Services;

namespace Lertaro.PluginSdk.Tests.Services;

// The branch that decides whether an "open directory" call reveals a named item or opens the folder, plus
// the host hand-off a plugin's own folder opens go through. Only decisions are covered here: every outcome
// ends in a hand-off to the shell, which needs a live desktop (and would open real windows in a test run).
[TestClass]
[DoNotParallelize]
public sealed class ExplorerServiceTests
{
    // Both are process-wide statics a host wires at startup, so every test here has to leave them as it
    // found them -- including before the first one, since a previous run can leave them behind.
    [TestInitialize]
    [TestCleanup]
    public void ResetHostDelegates()
    {
        ExplorerService.OpenFolderFunc = null;
        ExplorerService.OpenDirectoryFunc = null;
    }

    [TestMethod]
    public void ShouldRevealItem_ExistingNamedItem_IsRevealed() =>
        Assert.IsTrue(ExplorerService.ShouldRevealItem(@"C:\folder\file.txt", _ => true));

    [TestMethod]
    public void ShouldRevealItem_NamedItemThatIsGone_OpensTheDirectoryInstead() =>
        // Selecting an item that no longer exists is not a useful outcome; the folder open below it is.
        Assert.IsFalse(ExplorerService.ShouldRevealItem(@"C:\folder\gone.txt", _ => false));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void ShouldRevealItem_NoNamedItem_OpensTheDirectory(string? fileNameOrFilePath) =>
        // The probe would answer "yes" to everything, so it advertises that it must not be consulted.
        Assert.IsFalse(ExplorerService.ShouldRevealItem(fileNameOrFilePath, _ => true));

    [TestMethod]
    public void OpenFolder_HostRouteWired_HandsTheFolderToTheHost()
    {
        var opened = new List<string>();
        ExplorerService.OpenFolderFunc = opened.Add;

        ExplorerService.OpenFolder(@"C:\Users\testuser\Desktop");

        // The whole point of the seam: a plugin cannot apply the default file manager or the "new tab"
        // option itself, so the folder has to reach the host's route and nothing else.
        CollectionAssert.AreEqual(new[] { @"C:\Users\testuser\Desktop" }, opened);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void OpenFolder_NoFolder_AsksNothingOfTheHost(string? folderPath)
    {
        var called = false;
        ExplorerService.OpenFolderFunc = _ => called = true;

        ExplorerService.OpenFolder(folderPath);

        Assert.IsFalse(called, "the host was asked to open an empty path");
    }
}
