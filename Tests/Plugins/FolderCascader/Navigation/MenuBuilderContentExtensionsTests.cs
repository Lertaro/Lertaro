using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FolderCascader.Navigation;

namespace Lertaro.Plugins.FolderCascader.Tests.Navigation;

[TestClass]
public sealed class MenuBuilderContentExtensionsTests
{
    [TestMethod]
    public void HistoryEntryExists_File_OnlyUsesFileProbe()
    {
        var directoryProbed = false;
        var entry = new HistoryEntry(string.Empty, "item", HistoryEntryKind.File, 0);

        var exists = MenuBuilderContentExtensions.HistoryEntryExists(entry, _ => true, _ => directoryProbed = true);

        Assert.IsTrue(exists);
        Assert.IsFalse(directoryProbed);
    }

    [TestMethod]
    public void HistoryEntryExists_Folder_OnlyUsesDirectoryProbe()
    {
        var fileProbed = false;
        var entry = new HistoryEntry(string.Empty, "item", HistoryEntryKind.Folder, 0);

        var exists = MenuBuilderContentExtensions.HistoryEntryExists(entry, _ => fileProbed = true, _ => true);

        Assert.IsTrue(exists);
        Assert.IsFalse(fileProbed);
    }
}
