using Lertaro.App.Views.InlineSearchWindow.Helpers;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.InlineSearchWindow;

// Which folder-opens the inline window is allowed to hand to the host it is docked in, and which
// belong to the user's configured file manager instead. The distinction is not "is this Explorer" --
// the desktop IS Explorer, and matches the same adapter (Progman/WorkerW) -- it is whether there is a
// window to navigate in place at all. There isn't on the desktop, so Explorer answers by opening a new
// window, which is the exact act the setting exists to redirect.
[TestClass]
public sealed class InlineSearchNavigatorFileManagerTests
{
    private static DefaultFileManagerSetting Configured() => new() { Enabled = true, Path = @"C:\Tools\fm.exe" };

    [TestMethod]
    public void AFolderOpenedFromTheDesktop_GoesToTheConfiguredFileManager()
        => Assert.IsTrue(InlineSearchNavigator.OpeningAFolderBelongsToTheFileManager(isDesktop: true, isDir: true, Configured()));

    // Inside a real Explorer window the shortcut stays: navigating the window the user is already
    // standing in is what inline search is for, and is not an "open".
    [TestMethod]
    public void AFolderOpenedInsideAnExplorerWindow_StillNavigatesInPlace()
        => Assert.IsFalse(InlineSearchNavigator.OpeningAFolderBelongsToTheFileManager(isDesktop: false, isDir: true, Configured()));

    // The setting is about folders. A file opened from the desktop has nothing to do with it.
    [TestMethod]
    public void AFileOpenedFromTheDesktop_IsNotTheFileManagersBusiness()
        => Assert.IsFalse(InlineSearchNavigator.OpeningAFolderBelongsToTheFileManager(isDesktop: true, isDir: false, Configured()));

    [TestMethod]
    public void WithNoFileManagerConfigured_NothingIsRedirected()
    {
        Assert.IsFalse(InlineSearchNavigator.OpeningAFolderBelongsToTheFileManager(isDesktop: true, isDir: true, null));
        Assert.IsFalse(InlineSearchNavigator.OpeningAFolderBelongsToTheFileManager(isDesktop: true, isDir: true, new DefaultFileManagerSetting { Enabled = false, Path = @"C:\Tools\fm.exe" }));
    }

    // Switched on but never pointed at anything: redirecting there would open nothing at all, which is
    // worse than the shortcut it replaced.
    [TestMethod]
    public void SwitchedOnWithNoPath_IsNotRedirected()
    {
        Assert.IsFalse(InlineSearchNavigator.OpeningAFolderBelongsToTheFileManager(isDesktop: true, isDir: true, new DefaultFileManagerSetting { Enabled = true, Path = string.Empty }));
        Assert.IsFalse(InlineSearchNavigator.OpeningAFolderBelongsToTheFileManager(isDesktop: true, isDir: true, new DefaultFileManagerSetting { Enabled = true, Path = "   " }));
    }
}
