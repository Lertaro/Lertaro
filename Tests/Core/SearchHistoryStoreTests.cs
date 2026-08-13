using Lertaro.PluginSdk.Services;

namespace Lertaro.Core.Tests;

[TestClass]
public sealed class SearchHistoryStoreTests
{
    [TestMethod]
    public void ExistsForKind_File_OnlyUsesFileProbe()
    {
        var directoryProbed = false;

        var exists = SearchHistoryStore.ExistsForKind("item", HistoryEntryKind.File, _ => true, _ => directoryProbed = true);

        Assert.IsTrue(exists);
        Assert.IsFalse(directoryProbed);
    }

    [TestMethod]
    public void ExistsForKind_Folder_OnlyUsesDirectoryProbe()
    {
        var fileProbed = false;

        var exists = SearchHistoryStore.ExistsForKind("item", HistoryEntryKind.Folder, _ => fileProbed = true, _ => true);

        Assert.IsTrue(exists);
        Assert.IsFalse(fileProbed);
    }

    [TestMethod]
    public void ExistsForKind_Application_DoesNotProbeTheFilesystem()
    {
        var probed = false;

        var exists = SearchHistoryStore.ExistsForKind("item", HistoryEntryKind.Application, _ => probed = true, _ => probed = true);

        Assert.IsTrue(exists);
        Assert.IsFalse(probed);
    }

    [TestMethod]
    public void NormalizePath_WslPathUsesLexicalNormalization()
    {
        var path = @"\\wsl$\Ubuntu/home/testuser/~cache/";

        Assert.AreEqual(@"\\wsl$\Ubuntu\home\testuser\~cache", SearchHistoryStore.NormalizePath(path));
    }
}
