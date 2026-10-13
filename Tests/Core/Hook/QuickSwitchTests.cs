using Lertaro.Core.Hook;
using Lertaro.Core.Hook.Commands;
using Lertaro.Core.Hook.InlineSearch;
using Lertaro.Core.Hook.Ipc;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Registries;

namespace Lertaro.Core.Tests.Hook;

/// <summary>
/// Ctrl+G end to end, minus the two native edges: which folder it picks, when it declines, and -- the part
/// that used to fail -- that recognizing the gesture never waits on the tracker lock.
/// </summary>
[TestClass]
public sealed class QuickSwitchTests
{
    private static readonly IntPtr Dialog = new(0x1000);
    private static readonly IntPtr ExplorerA = new(0x2000);
    private static readonly IntPtr ExplorerB = new(0x3000);
    private static readonly IntPtr Gone = new(0x4000);

    private sealed class RecordingAdapter(bool navigate = true) : IFileDialogAdapter
    {
        internal List<(IntPtr Hwnd, string Path)> Navigations { get; } = [];
        internal bool Throw { get; init; }

        public bool CanHandle(IntPtr hwnd, string className, string processName) => true;
        public string? GetCurrentPath(IntPtr hwnd) => null;
        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect) { rect = default; return false; }
        public bool RestoreFocus(IntPtr hwnd) => true;

        public bool NavigateTo(IntPtr hwnd, string targetPath)
        {
            Navigations.Add((hwnd, targetPath));
            if (Throw) throw new InvalidOperationException("adapter fault");
            return navigate;
        }
    }

    // Sentinels are unique per test run, so an adapter registered here claims a handle nothing else uses.
    private static int _sentinels;
    private static IntPtr NextSentinel() => new(0x500000 + Interlocked.Increment(ref _sentinels));

    /// <summary>Stands in for a registry entry that answers for exactly one window.</summary>
    private sealed class SentinelAdapter(IntPtr hwnd, RecordingAdapter recorder) : IFileDialogAdapter
    {
        public bool CanHandle(IntPtr candidate, string className, string processName) => candidate == hwnd;
        public string? GetCurrentPath(IntPtr candidate) => recorder.GetCurrentPath(candidate);
        public bool GetDockBounds(IntPtr candidate, out AdapterRect rect) => recorder.GetDockBounds(candidate, out rect);
        public bool RestoreFocus(IntPtr candidate) => recorder.RestoreFocus(candidate);
        public bool NavigateTo(IntPtr candidate, string targetPath) => recorder.NavigateTo(candidate, targetPath);
    }

    private sealed class ProviderAdapter : IInlineSearchAdapter
    {
        public bool IsFileExplorer => true;
        public bool CanHandle(IntPtr hwnd, string className, string processName) => false;
        public bool CanTrigger(IntPtr hwnd, string className) => false;
        public string? GetSearchScope(IntPtr hwnd) => null;
        public bool ExecuteItem(IntPtr hwnd, string path, string searchInput) => false;
        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect) { rect = default; return false; }
        public bool CanEnterActionsMode(IntPtr hwnd) => false;
    }

    // A tracker whose window facts are the test's own, so a dialog and two file managers can exist without a
    // desktop. Handles are otherwise inert.
    private static ExplorerTracker Tracker(params (IntPtr Hwnd, uint ProcessId, bool Alive)[] windows)
    {
        var tracker = new ExplorerTracker();
        tracker.WindowIsAlive = hwnd => Array.Exists(windows, window => window.Hwnd == hwnd && window.Alive) || windows.Length == 0;
        tracker.WindowProcessId = hwnd => Array.Find(windows, window => window.Hwnd == hwnd).ProcessId;
        return tracker;
    }

    // The dialog is active while the browsed folder is the file manager's -- that is the whole case -- so the
    // path goes in flagged as the non-dialog one it came from, exactly as ExplorerWindowClassifier publishes it.
    private static ExplorerTracker DialogTracker(bool withAdapter = true, string? browsedFolder = null)
    {
        var tracker = Tracker((Dialog, 42, true));
        tracker.ActiveHwnd = Dialog;
        tracker.IsActiveWindowDialog = true;
        tracker.ActiveAdapter = withAdapter ? new RecordingAdapter() : null;
        if (browsedFolder != null) tracker.UpdatePath(browsedFolder, false, isDialog: false);
        return tracker;
    }

    [TestMethod]
    public void Execute_KnownFolder_NavigatesOnceWithATrailingSeparator()
    {
        using var tracker = DialogTracker(browsedFolder: @"D:\Work");
        var adapter = (RecordingAdapter)tracker.ActiveAdapter!;
        var handedOff = new List<(IntPtr Hwnd, string Path)>();

        // Synchronously, where production queues the work item: what this asserts is the window and the path
        // handed over. Which adapter ends up navigating is the work item's own question (PR #352), so the
        // seam hands over the tracker and the handle rather than an adapter from this snapshot.
        Assert.IsTrue(QuickSwitchNavigator.Execute(tracker, Dialog, (_, h, p) => handedOff.Add((h, p))));

        Assert.AreEqual((Dialog, @"D:\Work\"), Assert.ContainsSingle(handedOff));
        Assert.IsEmpty(adapter.Navigations);
    }

    [TestMethod]
    public void Execute_WindowThatIsNoLongerTheDialog_DoesNotNavigate()
    {
        using var tracker = DialogTracker(browsedFolder: @"D:\Work");
        tracker.IsActiveWindowDialog = false;
        var adapter = (RecordingAdapter)tracker.ActiveAdapter!;

        Assert.IsFalse(QuickSwitchNavigator.Execute(tracker, Dialog));
        Assert.IsEmpty(adapter.Navigations);
    }

    [TestMethod]
    public void Execute_DialogTheTrackerHasLetGoOf_DoesNotNavigate()
    {
        using var tracker = DialogTracker(browsedFolder: @"D:\Work");
        // Held before the tracker is told the dialog is gone: the point is that the adapter it kept is not
        // used once the window it was matched for is no longer the tracked one.
        var adapter = (RecordingAdapter)tracker.ActiveAdapter!;
        tracker.ActiveHwnd = new IntPtr(0x9999);

        Assert.IsFalse(QuickSwitchNavigator.Execute(tracker, Dialog));
        Assert.IsEmpty(adapter.Navigations);
    }

    // A dialog this process could not match an adapter for is still worth handing over: the window is tracked
    // as a dialog (the App's mirror claimed it), and asking the registry by handle is the only resolution that
    // is not a snapshot. PR #352's point, and the reason the adapter is no longer checked here.
    [TestMethod]
    public void Execute_NoAdapterInTheSnapshot_StillHandsTheWindowOver()
    {
        using var tracker = DialogTracker(withAdapter: false, browsedFolder: @"D:\Work");
        var handedOff = new List<(IntPtr Hwnd, string Path)>();

        Assert.IsTrue(QuickSwitchNavigator.Execute(tracker, Dialog, (_, h, p) => handedOff.Add((h, p))));

        Assert.AreEqual((Dialog, @"D:\Work\"), Assert.ContainsSingle(handedOff));
    }

    // The fallback is the tracker's adapter, and only for the handle it is still tracking: a handle the
    // tracker has let go of, with nothing in the registry claiming it either, is refused rather than navigated
    // with whatever adapter happened to be cached.
    [TestMethod]
    public void NavigateResolvingAdapter_AHandleNobodyClaims_IsRefusedWithoutNavigating()
    {
        using var tracker = DialogTracker(browsedFolder: @"D:\Work");
        var adapter = (RecordingAdapter)tracker.ActiveAdapter!;
        tracker.ActiveHwnd = new IntPtr(0x9999);

        FileDialogCommandHandler.NavigateResolvingAdapter(tracker, Dialog, @"D:\Work\");

        Assert.IsEmpty(adapter.Navigations);
    }

    // PR #352's point, and the reason the registry is asked first: the adapter the tracker holds was resolved
    // when it classified the window, and it re-derives that answer afterwards on purpose. Navigating with the
    // stale one gets a silent false from NavigateTo.
    [TestMethod]
    public void ResolveAdapter_TheRegistrysAnswerForTheHandle_BeatsTheTrackersStaleOne()
    {
        var sentinel = NextSentinel();
        var fromRegistry = new RecordingAdapter();
        // Claims exactly one handle -- this sentinel -- so it stays inert for every other test that shares the
        // static registry, which has no way to unregister an adapter.
        FileDialogAdapterRegistry.Register(new SentinelAdapter(sentinel, fromRegistry));

        using var tracker = DialogTracker();
        var stale = (RecordingAdapter)tracker.ActiveAdapter!;
        tracker.ActiveHwnd = sentinel;
        tracker.IsActiveWindowDialog = true;
        tracker.ActiveAdapter = stale;

        var resolved = FileDialogCommandHandler.ResolveAdapter(tracker, sentinel);

        Assert.IsInstanceOfType<SentinelAdapter>(resolved);
        Assert.AreNotSame(stale, resolved);
        // And it really is the registry's entry doing the work, not merely a different object.
        resolved!.NavigateTo(sentinel, @"D:\Work\");
        Assert.AreEqual((sentinel, @"D:\Work\"), Assert.ContainsSingle(fromRegistry.Navigations));
    }

    [TestMethod]
    public void Execute_NothingBrowsedYet_DoesNotNavigate()
    {
        using var tracker = DialogTracker();
        var adapter = (RecordingAdapter)tracker.ActiveAdapter!;

        Assert.IsFalse(QuickSwitchNavigator.Execute(tracker, Dialog));
        Assert.IsEmpty(adapter.Navigations);
    }

    [TestMethod]
    public void Execute_WindowTheDesktopNoLongerHas_DoesNotNavigate()
    {
        using var tracker = DialogTracker(browsedFolder: @"D:\Work");
        tracker.WindowIsAlive = _ => false;
        var adapter = (RecordingAdapter)tracker.ActiveAdapter!;

        Assert.IsFalse(QuickSwitchNavigator.Execute(tracker, Dialog));
        Assert.IsEmpty(adapter.Navigations);
    }

    [TestMethod]
    public void Execute_AdapterFault_IsHandedToTheWorkItemThatCannotLetItEscape()
    {
        using var tracker = DialogTracker(browsedFolder: @"D:\Work");
        var adapter = new RecordingAdapter { Throw = true };

        // The hand-off itself succeeds, and each attempt records before it throws, so the fault happened twice
        // and escaped neither time -- escaping a pool work item takes the whole hook process down with it.
        Assert.IsTrue(QuickSwitchNavigator.Execute(tracker, Dialog, (t, h, p) => FileDialogCommandHandler.NavigateLoggingFailures(adapter, h, p)));
        FileDialogCommandHandler.NavigateLoggingFailures(adapter, Dialog, @"D:\Work\");

        Assert.AreEqual(2, adapter.Navigations.Count);
        Assert.AreEqual((Dialog, @"D:\Work\"), adapter.Navigations[0]);
        Assert.AreEqual((Dialog, @"D:\Work\"), adapter.Navigations[1]);
    }

    [TestMethod]
    public void Execute_DecliningAdapter_IsStillNotAnEscape()
    {
        var adapter = new RecordingAdapter(navigate: false);
        FileDialogCommandHandler.NavigateLoggingFailures(adapter, Dialog, @"D:\Work\");
        Assert.ContainsSingle(adapter.Navigations);
    }

    // The two-file-managers report: one global path could only ever answer with whichever window was
    // classified last.
    [TestMethod]
    public void SelectQuickSwitchPath_ProviderInTheDialogsProcess_WinsOverTheMoreRecentlyUsedOne()
    {
        using var tracker = Tracker((Dialog, 42, true), (ExplorerA, 42, true), (ExplorerB, 7, true));
        var provider = new ProviderAdapter();
        tracker.RememberPathProvider(provider, ExplorerA, @"D:\SameProcess");
        tracker.RememberPathProvider(provider, ExplorerB, @"C:\OtherProcess");

        Assert.AreEqual(@"D:\SameProcess", tracker.SelectQuickSwitchPath(Dialog));
    }

    [TestMethod]
    public void SelectQuickSwitchPath_NoProviderInTheDialogsProcess_UsesTheMostRecentOne()
    {
        using var tracker = Tracker((Dialog, 42, true), (ExplorerA, 7, true), (ExplorerB, 7, true));
        var provider = new ProviderAdapter();
        tracker.RememberPathProvider(provider, ExplorerA, @"D:\First");
        tracker.RememberPathProvider(provider, ExplorerB, @"C:\MostRecent");

        Assert.AreEqual(@"C:\MostRecent", tracker.SelectQuickSwitchPath(Dialog));
    }

    [TestMethod]
    public void SelectQuickSwitchPath_ProviderWhoseFolderWasNeverRead_IsSkipped()
    {
        using var tracker = Tracker((Dialog, 42, true), (ExplorerA, 42, true), (ExplorerB, 7, true));
        var provider = new ProviderAdapter();
        tracker.RememberPathProvider(provider, ExplorerA, @"D:\Remembered");
        tracker.RememberPathProvider(provider, ExplorerB);

        Assert.AreEqual(@"D:\Remembered", tracker.SelectQuickSwitchPath(Dialog));
    }

    [TestMethod]
    public void SelectQuickSwitchPath_ClosedProvider_IsDroppedRatherThanAnswered()
    {
        using var tracker = Tracker((Dialog, 7, true), (ExplorerA, 7, false), (ExplorerB, 7, true));
        var provider = new ProviderAdapter();
        tracker.RememberPathProvider(provider, ExplorerA, @"D:\Closed");
        tracker.RememberPathProvider(provider, ExplorerB, @"C:\Open");

        Assert.AreEqual(@"C:\Open", tracker.SelectQuickSwitchPath(Dialog));
        // And once, not on every read: the dead entry is gone from the list.
        Assert.AreEqual(@"C:\Open", tracker.SelectQuickSwitchPath(Dialog));
    }

    [TestMethod]
    public void SelectQuickSwitchPath_NoProviderAtAll_FallsBackToTheLastBrowsedFolder()
    {
        using var tracker = Tracker((Dialog, 42, true));
        tracker.UpdatePath(@"D:\Browsed", false);

        Assert.AreEqual(@"D:\Browsed", tracker.SelectQuickSwitchPath(Dialog));
    }

    [TestMethod]
    public void SelectQuickSwitchPath_FolderChange_KeepsTheProviderEntryCurrent()
    {
        using var tracker = Tracker((Dialog, 42, true), (ExplorerA, 42, true));
        var provider = new ProviderAdapter();
        tracker.RememberPathProvider(provider, ExplorerA, @"D:\Old");
        tracker.ActiveHwnd = ExplorerA;
        tracker.UpdatePath(@"D:\New", false);

        Assert.AreEqual(@"D:\New", tracker.SelectQuickSwitchPath(Dialog));
    }

    // The gesture half: recognizing Ctrl+G must not wait for the lock the WinEvent thread holds while it reads
    // a file manager, or the keystroke is simply dropped.
    [TestMethod]
    public void CheckAndHandleQuickSwitch_DoesNotWaitForTheTrackerLock()
    {
        using var tracker = DialogTracker(browsedFolder: @"D:\Work");
        var requested = IntPtr.Zero;
        var detector = new GlobalHotkeyDetector(new UserSettings(), tracker, hwnd => requested = hwnd);

        using var held = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var holder = Task.Run(() =>
        {
            lock (tracker.StateLock)
            {
                held.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            }
        });

        try
        {
            Assert.IsTrue(held.Wait(TimeSpan.FromSeconds(5)));
            detector.OnKeyDown(0xA2);
            var triggered = detector.CheckAndHandleQuickSwitch(0x47, 0, out var consumeKey);

            Assert.IsTrue(triggered);
            Assert.IsTrue(consumeKey);
            Assert.AreEqual(Dialog, requested);
        }
        finally
        {
            release.Set();
            holder.Wait(TimeSpan.FromSeconds(5));
        }
    }

    // The hand-off into the tracker thread is the last step of the gesture, and the tracker thread is also the
    // one thing that can be gone: a hook that never started it (id 0) and one whose tracker has exited both
    // have to come back as "nothing was handed over", because the caller's only remaining move is to say so --
    // the keystroke has been claimed by then either way.
    [TestMethod]
    public void RequestQuickSwitch_WithoutATrackerThreadToHandItTo_ReportsFailure()
    {
        var exitedThreadId = 0u;
        var thread = new Thread(() => exitedThreadId = ExplorerNativeHooks.GetCurrentThreadId());
        thread.Start();
        thread.Join();

        Assert.AreNotEqual(0u, exitedThreadId);
        Assert.IsFalse(HookProcess.RequestQuickSwitch((int)exitedThreadId));
        Assert.IsFalse(HookProcess.RequestQuickSwitch(0));
    }

    [TestMethod]
    public void CheckAndHandleQuickSwitch_NoDialog_LeavesTheKeystrokeToTheHostWindow()
    {
        using var tracker = new ExplorerTracker();
        var requested = 0;
        var detector = new GlobalHotkeyDetector(new UserSettings(), tracker, _ => requested++);

        detector.OnKeyDown(0xA2);
        var triggered = detector.CheckAndHandleQuickSwitch(0x47, 0, out var consumeKey);

        Assert.IsFalse(triggered);
        Assert.IsFalse(consumeKey);
        Assert.AreEqual(0, requested);
    }

    [TestMethod]
    public void CheckAndHandleQuickSwitch_NothingBrowsedYet_LeavesTheKeystrokeToTheHostWindow()
    {
        using var tracker = DialogTracker();
        var requested = 0;
        var detector = new GlobalHotkeyDetector(new UserSettings(), tracker, _ => requested++);

        detector.OnKeyDown(0xA2);
        var triggered = detector.CheckAndHandleQuickSwitch(0x47, 0, out var consumeKey);

        Assert.IsFalse(triggered);
        Assert.IsFalse(consumeKey);
        Assert.AreEqual(0, requested);
    }

    [TestMethod]
    public void CheckAndHandleQuickSwitch_AnotherKeyWithControlHeld_IsNotTheHotkey()
    {
        using var tracker = DialogTracker(browsedFolder: @"D:\Work");
        var requested = 0;
        var detector = new GlobalHotkeyDetector(new UserSettings(), tracker, _ => requested++);

        detector.OnKeyDown(0xA2);
        Assert.IsFalse(detector.CheckAndHandleQuickSwitch(0x48, 0, out _));
        Assert.AreEqual(0, requested);
    }

    // A freshly connected App mirror gets the Quick Switch value too, not only the dialog's own path: the hook
    // is started for the App each time, so the mirror would otherwise stay empty until some later event.
    [TestMethod]
    public void PublishCurrentState_RepeatsTheBrowsedFolderForTheAppMirror()
    {
        using var tracker = Tracker((ExplorerA, 7, true));
        tracker.ActiveHwnd = ExplorerA;
        tracker.UpdatePath(@"D:\Browsed", false);
        var published = new List<(string Path, bool IsDialog)>();
        tracker.OnPathCaptured += (path, _, isDialog) => published.Add((path, isDialog));

        tracker.PublishCurrentState();

        Assert.Contains(item => item.Path == @"D:\Browsed" && !item.IsDialog, published);
    }
}
