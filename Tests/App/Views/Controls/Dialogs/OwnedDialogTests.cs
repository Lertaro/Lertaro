using System.Windows;
using System.Windows.Threading;

using Lertaro.App.Views.Controls.Dialogs;

namespace Lertaro.App.Tests.Views.Controls.Dialogs;

// Real windows, really shown, because the failure is not in any value this code returns -- it is
// ShowDialog never returning at all. An owned window is destroyed by Win32 along with its owner, and a
// modal dialog destroyed that way never runs the WPF close that pops ShowDialog's nested dispatcher
// frame and re-enables every window it disabled: the app is left frozen with nothing on screen to
// explain it. The only way to pin that is to close an owner underneath a live modal dialog and require
// the call to come back.
//
// [DoNotParallelize] for the same reason as the other window-level tests here: showing a dialog is
// process-wide, not local to the test doing it -- it disables every window on the thread and pumps a
// frame of its own.
[TestClass]
[DoNotParallelize]
public sealed class OwnedDialogTests
{
    private const int PumpTimeoutMs = 10_000;

    // A bounded wait, because the regression these guard against does not fail -- it hangs. Asserting
    // inline would take the whole run down with it; joining a thread reports it as one failed test with
    // a message that says what happened. Its own STA thread, and so its own Dispatcher, so the nested
    // frame ShowDialog pumps sees only what this test put in front of it.
    private static void InItsOwnDialogPump(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        Assert.IsTrue(
            thread.Join(PumpTimeoutMs),
            "ShowDialog never came back -- its nested frame is still pumping, which is the app frozen with every window disabled");

        if (failure != null)
            throw failure;
    }

    private static Window Offscreen() => new()
    {
        Width = 200,
        Height = 100,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None,
        // Far off any real desktop: these are only ever here to be owned and closed.
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = -32000,
        Top = -32000,
    };

    // Queued before ShowDialog is called, so it runs on the nested frame ShowDialog itself pumps --
    // ContentRendered is no good here, since a test host has no desktop to render onto.
    private static void WhileTheDialogIsUp(Action action) =>
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, action);

    // The question outlives the window it was asked from. A box the user is in the middle of reading
    // must not vanish because the panel behind it lost focus -- and vanishing would also answer it,
    // since a dialog that closed itself reads as a cancel to every caller.
    [TestMethod]
    public void ShowModal_OwnerClosedWhileTheDialogIsUp_KeepsTheDialogUp() => InItsOwnDialogPump(() =>
    {
        var owner = Offscreen();
        owner.Show();

        var dialog = Offscreen();
        dialog.Owner = owner;

        var stillUpAfterTheOwnerTriedToClose = false;
        WhileTheDialogIsUp(() =>
        {
            owner.Close();
            stillUpAfterTheOwnerTriedToClose = dialog.IsVisible;
            dialog.Close(); // the user finally answers
        });

        OwnedDialog.ShowModal(dialog);

        Assert.IsTrue(stillUpAfterTheOwnerTriedToClose, "the dialog was dismissed along with its owner");
    });

    // Held up, not cancelled: the owner still goes away, once the question has been answered.
    [TestMethod]
    public void ShowModal_OwnerClosedWhileTheDialogIsUp_ClosesTheOwnerAfterwards() => InItsOwnDialogPump(() =>
    {
        var owner = Offscreen();
        owner.Show();

        var dialog = Offscreen();
        dialog.Owner = owner;

        var ownerClosed = false;
        owner.Closed += (_, _) => ownerClosed = true;

        WhileTheDialogIsUp(() =>
        {
            owner.Close();
            dialog.Close();
        });

        OwnedDialog.ShowModal(dialog);

        Assert.IsTrue(ownerClosed, "the owner's close was cancelled and never carried out");
    });

    // The other half of the freeze: everything ShowDialog disabled on the way in has to be usable
    // again afterwards, or the windows that are left cannot even be closed.
    [TestMethod]
    public void ShowModal_OwnerClosedWhileTheDialogIsUp_LeavesTheOtherWindowsUsable() => InItsOwnDialogPump(() =>
    {
        var bystander = Offscreen();
        bystander.Show();

        var owner = Offscreen();
        owner.Show();

        var dialog = Offscreen();
        dialog.Owner = owner;

        WhileTheDialogIsUp(() =>
        {
            owner.Close();
            dialog.Close();
        });

        OwnedDialog.ShowModal(dialog);

        Assert.IsTrue(bystander.IsEnabled, "a window the dialog disabled on the way in was never re-enabled");
        bystander.Close();
    });

    // An owner that was never asked to close must not be closed by this.
    [TestMethod]
    public void ShowModal_OwnerLeftAlone_StaysOpen() => InItsOwnDialogPump(() =>
    {
        var owner = Offscreen();
        owner.Show();

        var dialog = Offscreen();
        dialog.Owner = owner;

        WhileTheDialogIsUp(() => dialog.Close());

        OwnedDialog.ShowModal(dialog);

        Assert.IsTrue(owner.IsVisible, "the owner was closed even though nothing asked it to");
        owner.Close();
    });

    // The ordinary path still has to work, and the owner must not be left reporting to a dialog that
    // has already been answered and closed.
    [TestMethod]
    public void ShowModal_DialogClosedNormally_LetsGoOfItsOwner() => InItsOwnDialogPump(() =>
    {
        var owner = Offscreen();
        owner.Show();

        var dialog = Offscreen();
        dialog.Owner = owner;

        WhileTheDialogIsUp(() => dialog.Close());

        OwnedDialog.ShowModal(dialog);

        // Closing the owner now must not reach into the closed dialog; if the handler were still
        // attached this is where it would throw.
        owner.Close();
    });

    [TestMethod]
    public void ShowModal_NoOwner_StillShowsAndReturns() => InItsOwnDialogPump(() =>
    {
        var dialog = Offscreen();

        WhileTheDialogIsUp(() => dialog.Close());

        OwnedDialog.ShowModal(dialog);
    });

    // Application.Current is null in a test host, which is exactly the "no WPF app running" case the
    // resolver has to answer for rather than throw on.
    [TestMethod]
    public void ResolveOwner_WithNoApplication_ReturnsNothing() => InItsOwnDialogPump(()
        => Assert.IsNull(OwnedDialog.ResolveOwner(Offscreen())));

    [TestMethod]
    public void Pick_PrefersTheActiveWindow() => InItsOwnDialogPump(() =>
    {
        var background = Offscreen();
        background.Show();
        var active = Offscreen();
        active.Show();
        active.Activate();

        var chosen = OwnedDialog.Pick(new[] { background, active }, main: null, Offscreen(), skip: null);

        Assert.AreSame(active.IsActive ? active : background, chosen);
    });

    // A window still in the list but never shown has no HWND: owning a dialog to it is owning it to
    // nothing, and the dialog is destroyed the moment it is created.
    [TestMethod]
    public void Pick_SkipsAWindowThatWasNeverShown() => InItsOwnDialogPump(() =>
    {
        var neverShown = Offscreen();
        var real = Offscreen();
        real.Show();

        Assert.AreSame(real, OwnedDialog.Pick(new[] { neverShown, real }, main: null, Offscreen(), skip: null));
    });

    // Stands in for MenuHelperWindow, which cannot be built in a test host (its constructor wants a
    // live application for the themed icon). What is under test is the rule, not that one window.
    private sealed class TransientHost : Window, ITransientHostWindow
    {
    }

    // A window that only hosts something for a moment vanishes with it, taking anything it owns along
    // -- a prompt opened from a shell menu item would flash up and disappear again with the menu.
    [TestMethod]
    public void Pick_NeverOwnsADialogToATransientHostWindow() => InItsOwnDialogPump(() =>
    {
        var anchor = new TransientHost { Width = 1, Height = 1, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -32000, Top = -32000 };
        anchor.Show();
        var real = Offscreen();
        real.Show();

        Assert.AreSame(real, OwnedDialog.Pick(new Window[] { anchor, real }, main: null, Offscreen(), skip: null));
        Assert.IsNull(OwnedDialog.Pick(new Window[] { anchor }, main: null, Offscreen(), skip: null));

        anchor.Close();
    });

    [TestMethod]
    public void Pick_HonoursTheCallersOwnExclusion() => InItsOwnDialogPump(() =>
    {
        var unwanted = Offscreen();
        unwanted.Show();
        var wanted = Offscreen();
        wanted.Show();

        var chosen = OwnedDialog.Pick(new[] { unwanted, wanted }, main: null, Offscreen(), skip: w => w == unwanted);

        Assert.AreSame(wanted, chosen);
    });

    [TestMethod]
    public void Pick_FallsBackToTheMainWindow() => InItsOwnDialogPump(() =>
    {
        var main = Offscreen();
        main.Show();

        Assert.AreSame(main, OwnedDialog.Pick(Array.Empty<Window>(), main, Offscreen(), skip: null));
    });

    [TestMethod]
    public void Pick_WithNothingUsable_ReturnsNothing() => InItsOwnDialogPump(()
        => Assert.IsNull(OwnedDialog.Pick(Array.Empty<Window>(), main: null, Offscreen(), skip: null)));
}
