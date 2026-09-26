using System.Diagnostics;
using Lertaro.Core.Hook;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Registries;

namespace Lertaro.Core.Tests.Hook;

[TestClass]
public sealed class ExplorerActivePathPollerTests
{
    // The registry is static and has no way to unregister one, so every stand-in below claims exactly its own
    // sentinel handle and stays inert for every other test that shares it.
    private static int _sentinels;
    private static IntPtr NextSentinel() => new(0x400000 + Interlocked.Increment(ref _sentinels));

    /// <summary>
    /// Stands in for a dialog's adapter in a process that has not managed to match it yet. Claims exactly one
    /// window -- its own sentinel handle -- so it stays inert for every other test that shares the static
    /// registry, which has no way to unregister one.
    /// </summary>
    private sealed class LateMatchingAdapter : IFileDialogAdapter
    {
        private readonly IntPtr _hwnd;
        private readonly int _asksBeforeMatch;
        private int _asks;

        internal LateMatchingAdapter(IntPtr hwnd, int asksBeforeMatch)
            => (_hwnd, _asksBeforeMatch) = (hwnd, asksBeforeMatch);

        public bool CanHandle(IntPtr hwnd, string className, string processName) =>
            hwnd == _hwnd && Interlocked.Increment(ref _asks) > _asksBeforeMatch;

        public string? GetCurrentPath(IntPtr hwnd) => null;
        public bool NavigateTo(IntPtr hwnd, string targetPath) => false;
        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect) { rect = default; return false; }
        public bool RestoreFocus(IntPtr hwnd) => false;
    }

    /// <summary>
    /// An adapter that claims one window for its own first N lookings and then lets go. Two of these, one
    /// closing as the other opens, reproduce what the registry does to a dialog that builds its child
    /// windows after it appears -- the answer changes from one adapter to the next without the window, its
    /// class, or its process saying anything different.
    /// </summary>
    private sealed class LateReleasingAdapter : IFileDialogAdapter
    {
        private readonly IntPtr _hwnd;
        private readonly int _claimsFirstAsks;
        private int _asks;

        internal LateReleasingAdapter(IntPtr hwnd, int claimsFirstAsks)
            => (_hwnd, _claimsFirstAsks) = (hwnd, claimsFirstAsks);

        public bool CanHandle(IntPtr hwnd, string className, string processName) =>
            hwnd == _hwnd && Interlocked.Increment(ref _asks) <= _claimsFirstAsks;

        public string? GetCurrentPath(IntPtr hwnd) => null;
        public bool NavigateTo(IntPtr hwnd, string targetPath) => false;
        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect) { rect = default; return false; }
        public bool RestoreFocus(IntPtr hwnd) => false;
    }

    [TestMethod]
    public void IsObservedWindowStillActive_RequiresTheSameNonZeroHandle()
    {
        Assert.IsTrue(ExplorerActivePathPoller.IsObservedWindowStillActive(new IntPtr(1), new IntPtr(1)));
        Assert.IsFalse(ExplorerActivePathPoller.IsObservedWindowStillActive(new IntPtr(1), new IntPtr(2)));
        Assert.IsFalse(ExplorerActivePathPoller.IsObservedWindowStillActive(IntPtr.Zero, IntPtr.Zero));
    }

    [TestMethod]
    public void UpdatePath_ADialogClaim_KeepsTheDialogVerdictWithoutAnAdapter()
    {
        // The claim comes from the process that owns the WinEvent path; this process may have matched no
        // adapter because its own cross-process read timed out while the other side was still building.
        // The card is what waits on that verdict, so the claim wins over the weaker local answer -- but only
        // while a window is actually tracked, which is why the handle is set here rather than left empty.
        using var tracker = new ExplorerTracker { ActiveHwnd = new IntPtr(0x1234) };

        bool? reported = null;
        tracker.OnPathCaptured += (_, _, isDialog) => reported = isDialog;
        tracker.UpdatePath(@"D:\Downloads", isDesktop: false, isDialog: true);

        Assert.IsTrue(tracker.IsActiveWindowDialog);
        Assert.AreEqual(true, reported);
    }

    [TestMethod]
    public void UpdatePath_ADialogClaimAfterDeactivation_DoesNotResurrectAnything()
    {
        // A path event still in flight after the tracker let the window go must not leave it claiming a
        // dialog with no dialog to point at: everything downstream reads ActiveHwnd alongside the flag.
        using var tracker = new ExplorerTracker();

        tracker.UpdatePath(@"D:\Downloads", isDesktop: false, isDialog: true);

        Assert.IsFalse(tracker.IsActiveWindowDialog);
    }

    [TestMethod]
    public void UpdatePath_WithoutAClaim_LeavesTheVerdictAlone()
    {
        // The path collector calls this with isDialog omitted: a plain Explorer window's path must not be
        // read as "a dialog", or the card would filter to folders over an ordinary window.
        using var tracker = new ExplorerTracker();

        tracker.UpdatePath(@"D:\Downloads", isDesktop: false);

        Assert.IsFalse(tracker.IsActiveWindowDialog);
    }

    [TestMethod]
    public void UpdatePath_UsesConfiguredPathNormalizer()
    {
        using var tracker = new ExplorerTracker { PathNormalizer = _ => string.Empty };

        tracker.UpdatePath(@"D:\Projects", isDesktop: false);

        Assert.AreEqual(string.Empty, tracker.ActivePath);
    }

    [TestMethod]
    public void BudgetFor_DifferentWindow_RestartsTheCountdown()
    {
        // The retry that claims a common dialog which answered "not a file dialog" too early is bounded, and
        // the bound belongs to the window being asked about: a dialog opened later must not inherit an
        // exhausted budget from one the user gave up on.
        Assert.AreEqual(ExplorerActivePathPoller.UnclaimedDialogRetryLimit,
            ExplorerActivePathPoller.BudgetFor(new IntPtr(2), new IntPtr(1), 0));
        Assert.AreEqual(3, ExplorerActivePathPoller.BudgetFor(new IntPtr(1), new IntPtr(1), 3),
            "the same dialog keeps counting down, which is what stops it being asked forever");
    }

    [TestMethod]
    public void BudgetFor_NoForegroundOrFreshTracking_RestartsTheCountdown()
    {
        // Reached once the dialog is claimed (nothing left to retry) or the foreground is some other kind of
        // window; both have to leave the counter ready for the next common dialog.
        Assert.AreEqual(ExplorerActivePathPoller.UnclaimedDialogRetryLimit,
            ExplorerActivePathPoller.BudgetFor(IntPtr.Zero, new IntPtr(1), 0));
        Assert.AreEqual(ExplorerActivePathPoller.UnclaimedDialogRetryLimit,
            ExplorerActivePathPoller.BudgetFor(new IntPtr(2), IntPtr.Zero, ExplorerActivePathPoller.UnclaimedDialogRetryLimit));
    }

    [TestMethod]
    public void UpdatePath_DialogSource_DoesNotReplaceLastExplorerPath()
    {
        using var tracker = new ExplorerTracker();

        tracker.UpdatePath(@"C:\Workspace", isDesktop: false, isDialog: false);
        tracker.UpdatePath(@"D:\Downloads", isDesktop: false, isDialog: true);

        Assert.AreEqual(@"C:\Workspace", tracker.LastActiveExplorerPath);
        Assert.AreEqual(@"D:\Downloads", tracker.ActivePath);
    }

    [TestMethod]
    public void UpdatePath_ADialogClaimThisProcessCouldNotMatch_ReAsksUntilAnAdapterAnswers()
    {
        // The claim comes from the process that owns the WinEvent path, but the card is placed by this one,
        // and its single adapter read ran in the instant the activation was mirrored. For a dialog still
        // building the child windows an adapter looks for, that read is simply too early -- and nothing asks
        // again until the next activation, so the window stays a dialog this process can name but cannot
        // measure. One that cannot answer for its own file list leaves the card hanging over the middle of
        // the dialog instead of off that list's right edge, until the user moves the focus away and back.
        var hwnd = NextSentinel();
        var adapter = new LateMatchingAdapter(hwnd, asksBeforeMatch: 2);
        FileDialogAdapterRegistry.Register(adapter);

        using var tracker = new ExplorerTracker { ActiveHwnd = hwnd };
        Assert.IsNull(tracker.ActiveAdapter, "the read that ran with the activation is the one that was too early");

        using var replaced = new ManualResetEventSlim(false);
        tracker.OnActiveWindowMoved += () => replaced.Set();

        tracker.UpdatePath(@"D:\Downloads", isDesktop: false, isDialog: true);

        Assert.IsTrue(replaced.Wait(TimeSpan.FromSeconds(15)),
            "nothing re-asked, so the card keeps the anchorless placement until a focus change manufactures a fresh activation");
        Assert.AreSame(adapter, tracker.ActiveAdapter, "the re-ask is what this process's own measurement needs");
        Assert.IsTrue(tracker.IsActiveWindowDialog);
    }

    [TestMethod]
    public void ADialogThisProcessCannotMeasure_IsReAskedWithoutWaitingForAClaim()
    {
        // A claim from the process that owns the WinEvent path is only one route into "a dialog is active
        // here, and nothing here can measure it". Measured on a live Rimage 添加文件, the tracker sat in that
        // state for thirteen seconds without ever taking the claim branch, and the card hung over the middle
        // of the dialog until a focus change manufactured the fresh activation that finally matched an
        // adapter. So the repair keys on the state, and anything that observes it may ask -- the placement
        // pass does, on every measurement the geometry probe takes off the thread that places the card.
        var hwnd = NextSentinel();
        var adapter = new LateMatchingAdapter(hwnd, asksBeforeMatch: 2);
        FileDialogAdapterRegistry.Register(adapter);

        using var tracker = new ExplorerTracker { ActiveHwnd = hwnd, IsActiveWindowDialog = true };
        Assert.IsNull(tracker.ActiveAdapter, "the read that ran with the activation is the one that was too early");

        using var replaced = new ManualResetEventSlim(false);
        tracker.OnActiveWindowMoved += () => replaced.Set();

        // Asked repeatedly, as a placement pass would; one re-derivation chain per window, not one per ask.
        for (var pass = 0; pass < 5; pass++) tracker.RederiveActiveDialogAdapterIfStale();

        Assert.IsTrue(replaced.Wait(TimeSpan.FromSeconds(15)),
            "nothing re-asked, so the card keeps the anchorless placement until the user moves the focus away and back");
        Assert.AreSame(adapter, tracker.ActiveAdapter);
    }

    [TestMethod]
    public void ADialogClaimedByTheWrongAdapterIsReDerivedWithoutWaitingForAFocusChange()
    {
        // The interesting state is not a missing adapter, it is a dialog claimed by one that has no file list
        // to report. The two adapters dividing the common dialogs do so on a child window built after the
        // dialog appears, so the registry's first answer is the wrong adapter -- and because ActiveAdapter is
        // not null, an absence check calls this healthy. On a live Rimage 添加文件夹 the card sat there with
        // list=none for the whole life of the dialog, and the only thing that ended it was a fresh activation.
        var hwnd = NextSentinel();
        FileDialogAdapterRegistry.Register(new LateReleasingAdapter(hwnd, claimsFirstAsks: 1));
        FileDialogAdapterRegistry.Register(new LateReleasingAdapter(hwnd, claimsFirstAsks: int.MaxValue));

        using var tracker = new ExplorerTracker { ActiveHwnd = hwnd };
        var claimedFirst = tracker.ActiveAdapter;
        Assert.IsNotNull(claimedFirst, "the adapter registered first wins the half-built dialog, and that is the bug");

        using var replaced = new ManualResetEventSlim(false);
        tracker.OnActiveWindowMoved += () => replaced.Set();

        tracker.RederiveActiveDialogAdapterIfStale();

        Assert.IsTrue(replaced.Wait(TimeSpan.FromSeconds(15)),
            "the registry was never asked again, so the dialog stays measured with an adapter that cannot see it");
        Assert.AreNotSame(claimedFirst, tracker.ActiveAdapter, "a different adapter is what the repair is for");
        Assert.IsTrue(tracker.IsActiveWindowDialog);
    }

    [TestMethod]
    public void UpdatePath_ADialogClaim_IsNotHeldUpByTheReAsk()
    {
        // In the App the claim arrives on the IPC mirror thread, which carries every event the hook sends.
        // Re-asking is a bounded cross-process read repeated on a timer, so it belongs on its own thread:
        // inlining it would hold the mirror for the whole retry budget, which is what commit 2acff94 was
        // written to stop. A stand-in that never matches spends every re-ask, so an inlined loop would take
        // the full gap times the limit here rather than returning at once.
        var hwnd = NextSentinel();
        FileDialogAdapterRegistry.Register(new LateMatchingAdapter(hwnd, asksBeforeMatch: int.MaxValue));

        using var tracker = new ExplorerTracker { ActiveHwnd = hwnd };

        var claimed = Stopwatch.StartNew();
        tracker.UpdatePath(@"D:\Downloads", isDesktop: false, isDialog: true);
        claimed.Stop();

        Assert.IsTrue(tracker.IsActiveWindowDialog);
        Assert.IsNull(tracker.ActiveAdapter, "this dialog is never measurable, so there is nothing to apply");
        Assert.IsLessThan(1000L, claimed.ElapsedMilliseconds,
            "the claim waited on the re-ask, so it ran on the thread that carries every event");
    }
}
