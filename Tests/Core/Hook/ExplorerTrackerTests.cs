using Lertaro.Core.Hook;

namespace Lertaro.Core.Tests.Hook;

[TestClass]
public sealed class ExplorerTrackerTests
{
    // Quick Switch's dialog route reads the dialog-time memory (LastActiveExplorerPath), which is only
    // written once a dialog has appeared in this process's lifetime. An App started after the file manager
    // window was already open therefore has nothing there, and falls back to LastFileManagerPath.
    [TestMethod]
    public void LastPathRemembersTheLastFileManagerFolder()
    {
        var tracker = new ExplorerTracker { IsActiveWindowDialog = false };

        tracker.LastPath = @"D:\Projects\";

        Assert.AreEqual(@"D:\Projects\", tracker.LastFileManagerPath);
    }

    // A dialog's own folder is not a folder the user "came from": remembering it would make the fallback
    // navigate the dialog to where it already is, which reads as the hotkey doing nothing.
    [TestMethod]
    public void LastPathDoesNotRememberADialogsFolder()
    {
        var tracker = new ExplorerTracker { IsActiveWindowDialog = true };

        tracker.LastPath = @"D:\Projects\";

        Assert.IsNull(tracker.LastFileManagerPath);
    }

    [TestMethod]
    public void LastPathKeepsTheMostRecentFolderAndIgnoresEmptyValues()
    {
        var tracker = new ExplorerTracker { IsActiveWindowDialog = false };

        tracker.LastPath = @"D:\one\";
        tracker.LastPath = @"D:\two\";
        tracker.LastPath = string.Empty;

        Assert.AreEqual(@"D:\two\", tracker.LastFileManagerPath);
    }
}
