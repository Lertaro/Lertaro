using System.Windows;
using System.Windows.Interop;
using Lertaro.App.Views.Controls.Dialogs;
using Lertaro.App.Views.InlineSearchWindow.Helpers;

namespace Lertaro.App.Tests.Services.InlineSearch;

// The inline card outlives the summon that built it, and while it is on screen it is owned by the host window it
// was docked to -- raw GWL_HWNDPARENT, see InlineSearchWindow.HostOnto. The system destroys an owned window along
// with its owner, and WPF reads that WM_DESTROY as a close. So a card dismissed while still docked to a file
// dialog goes away with that dialog, and the window the manager kept holding is one that Show and EnsureHandle
// both refuse to touch: the "关闭窗口后，无法设置可见性" crash report on the next summon. What the fix relies on is
// measured here on real windows -- the dock is what kills the parked card, releasing it is what saves it, and the
// handle test that gates every deferred Show is what tells the two states apart.
[TestClass]
[DoNotParallelize]
public sealed class ParkedCardHostOwnershipTests
{
    private static Window Offscreen() => new()
    {
        Width = 200,
        Height = 100,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = -32000,
        Top = -32000,
    };

    private static IntPtr HandleOf(Window window) => new WindowInteropHelper(window).Handle;

    [STATestMethod]
    public void ParkedCard_StillDockedToItsHost_IsClosedWhenTheHostCloses()
    {
        var host = Offscreen();
        host.Show();

        var card = Offscreen();
        card.Show();
        card.Hide();
        InlineSearchWindowNativeMethods.SetWindowLongPtr(HandleOf(card),
            InlineSearchWindowNativeMethods.GWL_HWNDPARENT, HandleOf(host));

        var cardWasClosed = false;
        card.Closed += (_, _) => cardWasClosed = true;

        host.Close();

        Assert.IsTrue(cardWasClosed, "the host's destruction did not reach the card still docked to it");
        Assert.ThrowsExactly<InvalidOperationException>(() => new WindowInteropHelper(card).EnsureHandle());
    }

    // Why the audit only has to look at show-sites: a window the system destroyed with its owner reports no
    // handle and refuses Show / EnsureHandle / Visibility=Visible, while Close() on it is a silent no-op. So a
    // cached corpse reached only by Close() or Hide() is harmless, and one reached by a show is the crash.
    [STATestMethod]
    public void DestroyedWithItsHost_ClosesSilentlyButReportsNoHandle()
    {
        var host = Offscreen();
        host.Show();

        var card = Offscreen();
        card.Show();
        InlineSearchWindowNativeMethods.SetWindowLongPtr(HandleOf(card),
            InlineSearchWindowNativeMethods.GWL_HWNDPARENT, HandleOf(host));

        host.Close();

        Assert.AreEqual(IntPtr.Zero, HandleOf(card), "the destroyed card still reported a handle");
        card.Close();   // the no-op every other dismissal path in this app already relies on
        Assert.ThrowsExactly<InvalidOperationException>(() => card.Show());
    }

    // The gate itself, as production reads it (OwnedDialog.IsAlive, now also what QuickLookManager's deferred
    // preview restore runs before each Show). It has to reject the window the system destroyed with its owner and
    // keep the one that is merely hidden -- a hidden window is the normal state between two summons.
    [STATestMethod]
    public void IsAlive_RejectsTheWindowDestroyedWithItsOwner_AndKeepsTheHiddenOne()
    {
        var host = Offscreen();
        host.Show();

        var docked = Offscreen();
        docked.Show();
        docked.Hide();
        InlineSearchWindowNativeMethods.SetWindowLongPtr(HandleOf(docked),
            InlineSearchWindowNativeMethods.GWL_HWNDPARENT, HandleOf(host));

        var justHidden = Offscreen();
        justHidden.Show();
        justHidden.Hide();

        host.Close();

        Assert.IsFalse(OwnedDialog.IsAlive(docked), "a card destroyed with its host still read as alive");
        Assert.IsTrue(OwnedDialog.IsAlive(justHidden), "a hidden card of a still living host read as dead");
        justHidden.Show();
        Assert.IsTrue(justHidden.IsVisible, "the card the gate kept could not be shown again");
        justHidden.Close();
    }

    [STATestMethod]
    public void ParkedCard_ReleasedFromItsHost_SurvivesTheHostAndSummonsAgain()
    {
        var host = Offscreen();
        host.Show();

        var card = Offscreen();
        card.Show();
        card.Hide();
        InlineSearchWindowNativeMethods.SetWindowLongPtr(HandleOf(card),
            InlineSearchWindowNativeMethods.GWL_HWNDPARENT, HandleOf(host));
        InlineSearchWindowNativeMethods.SetWindowLongPtr(HandleOf(card),
            InlineSearchWindowNativeMethods.GWL_HWNDPARENT, IntPtr.Zero);

        var cardWasClosed = false;
        card.Closed += (_, _) => cardWasClosed = true;

        host.Close();

        Assert.IsFalse(cardWasClosed, "the card died with the host it had been released from");
        Assert.AreNotEqual(IntPtr.Zero, HandleOf(card), "the card lost its handle along with its owner");
        card.Show();
        Assert.IsTrue(card.IsVisible, "a surviving card cannot be shown again");
        card.Close();
    }
}
