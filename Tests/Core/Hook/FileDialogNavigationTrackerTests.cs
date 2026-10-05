using Lertaro.Core.Hook;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.Core.Tests.Hook;

// The dialog follow answers one question: when a file dialog comes back to the foreground, should it be
// pointed at the folder the file manager is showing, and at which folder. It decides from state the tracker
// keeps (the last provider path and when that was written), so these cases drive it through that state
// instead of through a live window -- the window half is what the calling classifier supplies.
[TestClass]
public sealed class FileDialogNavigationTrackerTests
{
    private static readonly IntPtr Dialog = new(0x1000);
    private static readonly IntPtr OtherDialog = new(0x2000);

    private sealed class RecordingAdapter : IFileDialogAdapter
    {
        private readonly List<(IntPtr Hwnd, string Path)> _navigations = [];

        public bool CanHandle(IntPtr hwnd, string className, string processName) => true;
        public string? GetCurrentPath(IntPtr hwnd) => null;
        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect) { rect = default; return false; }
        public bool RestoreFocus(IntPtr hwnd) => true;

        public bool NavigateTo(IntPtr hwnd, string targetPath)
        {
            lock (_navigations) _navigations.Add((hwnd, targetPath));
            return true;
        }

        // The tracker navigates on a worker thread, so the assertion waits for it rather than assuming it.
        public (IntPtr Hwnd, string Path)? WaitForOne(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (_navigations)
                {
                    if (_navigations.Count > 0) return _navigations[0];
                }

                Thread.Sleep(20);
            }

            return null;
        }

        public int Count { get { lock (_navigations) return _navigations.Count; } }
    }

    // A first sighting is the dialog appearing, not the dialog being returned to: navigating there would
    // move the dialog out from under the click that opened it.
    [TestMethod]
    public void HandleDialogSeen_FirstSighting_NavigatesNothing()
    {
        var tracker = new FileDialogNavigationTracker();
        tracker.SetLastActiveExplorerPath(@"D:\Projects\音乐");
        var adapter = new RecordingAdapter();

        tracker.HandleDialogSeen(Dialog, adapter, previousWasPathProvider: true);

        Assert.AreEqual(0, adapter.Count);
    }

    // The whole point of the feature: the user browsed elsewhere in the file manager after the dialog
    // appeared, then came back, and the dialog follows to that folder.
    [TestMethod]
    public void HandleDialogSeen_PathUpdatedAfterTheDialogAppeared_NavigatesToThatPath()
    {
        var tracker = new FileDialogNavigationTracker();
        var adapter = new RecordingAdapter();
        tracker.HandleDialogSeen(Dialog, adapter, previousWasPathProvider: false);

        // The tracker compares timestamps taken from the wall clock, whose resolution is a few milliseconds.
        Thread.Sleep(30);
        tracker.SetLastActiveExplorerPath(@"D:\Projects\音乐\星巢音乐");

        tracker.HandleDialogSeen(Dialog, adapter, previousWasPathProvider: false);

        var navigation = adapter.WaitForOne(TimeSpan.FromSeconds(2));
        Assert.IsNotNull(navigation, "the dialog should have been navigated");
        Assert.AreEqual(Dialog, navigation!.Value.Hwnd);
        Assert.AreEqual(@"D:\Projects\音乐\星巢音乐", navigation.Value.Path);
    }

    // The other half of the condition: the window in front of the dialog was a file manager, so the stored
    // path is the one the user was just looking at even though nothing wrote it after the dialog appeared.
    [TestMethod]
    public void HandleDialogSeen_PreviousWindowWasAPathProvider_NavigatesWithoutAPathUpdate()
    {
        var tracker = new FileDialogNavigationTracker();
        var adapter = new RecordingAdapter();
        tracker.SetLastActiveExplorerPath(@"D:\Projects\音乐");
        tracker.HandleDialogSeen(Dialog, adapter, previousWasPathProvider: false);

        tracker.HandleDialogSeen(Dialog, adapter, previousWasPathProvider: true);

        var navigation = adapter.WaitForOne(TimeSpan.FromSeconds(2));
        Assert.IsNotNull(navigation, "the dialog should have been navigated");
        Assert.AreEqual(@"D:\Projects\音乐", navigation!.Value.Path);
    }

    // Nothing to follow: neither a stored path nor a file manager in front of the dialog. The dialog must be
    // left where the user put it rather than being navigated to an empty path.
    [TestMethod]
    public void HandleDialogSeen_NothingStoredAndNoProvider_NavigatesNothing()
    {
        var tracker = new FileDialogNavigationTracker();
        var adapter = new RecordingAdapter();
        tracker.HandleDialogSeen(Dialog, adapter, previousWasPathProvider: false);

        tracker.HandleDialogSeen(Dialog, adapter, previousWasPathProvider: false);

        Assert.AreEqual(0, adapter.Count);
    }

    // Each dialog carries its own first-sighting time: opening a second dialog must not look like the first
    // one being returned to.
    [TestMethod]
    public void HandleDialogSeen_SecondDialogIsItsOwnFirstSighting()
    {
        var tracker = new FileDialogNavigationTracker();
        var adapter = new RecordingAdapter();
        tracker.SetLastActiveExplorerPath(@"D:\Projects");
        tracker.HandleDialogSeen(Dialog, adapter, previousWasPathProvider: true);

        Thread.Sleep(30);
        tracker.SetLastActiveExplorerPath(@"D:\Projects\音乐");

        tracker.HandleDialogSeen(OtherDialog, adapter, previousWasPathProvider: true);

        Assert.AreEqual(0, adapter.Count, "a newly seen dialog is not a reactivation");
    }
}
