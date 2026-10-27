using System.Runtime.InteropServices;

using Lertaro.Plugins.CoreExtensions.Preview.Handlers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Preview.Handlers;

// Which dialogs PreviewFocusGuard's EVENT_SYSTEM_DIALOGSTART watcher is allowed to react to. The two arms
// differ in what they know: the one taken before the handler is handed the file has no PID to filter on, the
// one taken once the handler has parented its render window does. Both must ignore this app's own windows --
// reacting to one would hide the search window while the user is looking at a settings page.
[TestClass]
public sealed class PreviewDialogWatcherScopeTests
{
    [TestMethod]
    [DataRow(0u, 4321u, false, true)] // render-scoped: any other process counts
    [DataRow(0u, 4321u, true, false)] // ... but not a window this app showed
    [DataRow(4321u, 4321u, false, true)] // session-scoped: the handler's own process
    [DataRow(4321u, 9999u, false, false)] // ... and nothing else once that PID is known
    [DataRow(4321u, 4321u, true, false)]
    public void ShouldSurfaceDialog_ReactsOnlyToForeignDialogs(uint watcherPid, uint dialogPid, bool isOwnWpfWindow, bool expected) =>
        // The last row is the case that used to be safe by construction alone: an in-process handler parents
        // its window from this same PID, so the watcher ends up armed on our own PID and every WPF dialog the
        // app itself shows passes that filter too.
        Assert.AreEqual(expected, PreviewFocusGuard.ShouldSurfaceDialog(watcherPid, dialogPid, isOwnWpfWindow));

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [TestMethod]
    public void IsOwnWpfWindow_DoesNotClaimAnotherProcessesWindow()
    {
        // The fix rests on this: HwndSource answers only for handles this process created through WPF. Were
        // it to answer for a foreign window, every dialog the guard exists to surface would be filtered out
        // as one of ours, and the app would go on looking hung with nothing to show for it.
        Assert.IsFalse(PreviewFocusGuard.IsOwnWpfWindow(GetDesktopWindow()));
        Assert.IsFalse(PreviewFocusGuard.IsOwnWpfWindow(IntPtr.Zero));
    }
}
