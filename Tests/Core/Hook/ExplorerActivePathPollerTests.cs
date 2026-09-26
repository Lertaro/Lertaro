using Lertaro.Core.Hook;

namespace Lertaro.Core.Tests.Hook;

[TestClass]
public sealed class ExplorerActivePathPollerTests
{
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
}
