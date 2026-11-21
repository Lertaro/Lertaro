using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Lertaro.App.Helpers.Visuals;

using Application = System.Windows.Application;

namespace Lertaro.App.Views.Controls.Dialogs;

/// <summary>
/// Picking an owner for a modal dialog, and showing it in a way that survives that owner being closed
/// out from under it.
/// </summary>
/// <remarks>
/// Shared by every custom dialog in the app rather than copied per dialog: the owner-resolution chain
/// was already duplicated, and the failure below is the kind that is only ever found once, in whichever
/// copy the user happened to hit.
/// </remarks>
internal static class OwnedDialog
{
    /// <summary>
    /// The window a dialog should belong to: the active one, else any visible one, else the main
    /// window, else none (and it centres on screen instead).
    /// </summary>
    /// <param name="skip">
    /// An extra exclusion on top of the dialog itself -- e.g. a message box declining to sit on top of
    /// another message box.
    /// </param>
    public static Window? ResolveOwner(Window dialog, Func<Window, bool>? skip = null)
        => Application.Current == null
            ? null
            : Pick(Application.Current.Windows.Cast<Window>(), Application.Current.MainWindow, dialog, skip);

    /// <summary>The chain itself, over a given set of windows rather than the live application's.</summary>
    internal static Window? Pick(IEnumerable<Window> windows, Window? main, Window dialog, Func<Window, bool>? skip)
    {
        var candidates = windows.ToList();

        bool Candidate(Window w) => w != dialog && CanOwnADialog(w) && (skip == null || !skip(w));

        foreach (var w in candidates)
        {
            if (w.IsActive && w.IsVisible && Candidate(w))
                return w;
        }

        foreach (var w in candidates)
        {
            if (w.IsVisible && Candidate(w))
                return w;
        }

        return main != null && main.IsVisible && Candidate(main) ? main : null;
    }

    // A parked or transparent window may still have WS_VISIBLE. It cannot supply a useful centre
    // for a dialog, even when it was not declared as a menu anchor (issue #516).
    internal static bool CanOwnADialog(Window window) => window is not ITransientHostWindow
        && IsAlive(window) && window.IsVisible && window.Opacity > 0
        && window.WindowState != WindowState.Minimized && window.Top != WindowParking.OffScreen;

    /// <summary>Shows <paramref name="dialog"/> modally and defers closing its owner until it returns.</summary>
    /// <remarks>
    /// Destroying an owner also destroys its native owned windows. If that bypasses WPF's modal close,
    /// ShowDialog's nested dispatcher frame and disabled windows can remain active, freezing the app.
    ///
    /// Cancel the owner's Closing event while the dialog is open, then replay the close after ShowDialog
    /// returns. Keep the user's pending question visible; WPF cannot reassign Owner during ShowDialog.
    /// Application shutdown can bypass this cancellation.
    /// </remarks>
    public static void ShowModal(Window dialog)
    {
        // Every custom dialog in the app is built here and shown from here, so this is the one place its first
        // frame can be kept out of sight: an opaque borderless dialog is the window class's black until the
        // render thread presents, and a dialog made of a plugin-file list is a heavy first tree.
        WindowParking.ParkUntilPainted(dialog);

        var owner = dialog.Owner;
        if (owner == null)
        {
            dialog.ShowDialog();
            return;
        }

        var ownerCloseDeferred = false;

        void OnOwnerClosing(object? sender, CancelEventArgs e)
        {
            // Already cancelled by somebody else: their veto stands, and re-issuing the close below
            // would override a decision that was not ours to make.
            if (e.Cancel)
                return;

            e.Cancel = true;
            ownerCloseDeferred = true;
        }

        owner.Closing += OnOwnerClosing;
        try
        {
            dialog.ShowDialog();
        }
        finally
        {
            // Also on the normal path, or a dialog answered and gone would keep its owner reporting to
            // it for as long as that owner lives.
            owner.Closing -= OnOwnerClosing;
        }

        // Now that the modal frame has unwound, carry out the close the owner asked for while it was
        // held up. Guarded, because answering the dialog may well have closed it already.
        if (ownerCloseDeferred && IsAlive(owner))
            owner.Close();
    }

    // A window still listed in Application.Current.Windows but with no HWND is one that has already
    // been closed, or was never shown. Owning a dialog to it is owning it to nothing: the dialog is
    // destroyed the moment it is created, which is the same freeze as above with no window to blame.
    // Internal because the same test is what gates every deferred Show: a window the system destroyed
    // along with its owner reads as closed here, and Show on it throws.
    internal static bool IsAlive(Window window) => new WindowInteropHelper(window).Handle != IntPtr.Zero;
}
