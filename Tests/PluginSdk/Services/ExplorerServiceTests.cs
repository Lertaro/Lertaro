using Lertaro.PluginSdk.Services;

namespace Lertaro.PluginSdk.Tests.Services;

// The branch that decides whether an "open directory" call reveals a named item or opens the folder.
// Only the decision is covered here: both outcomes end in a hand-off to the shell, which needs a live
// desktop (and would open real windows if a test exercised it).
[TestClass]
public sealed class ExplorerServiceTests
{
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
}
