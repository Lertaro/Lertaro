using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;

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

        bool Candidate(Window w) => w != dialog && IsAlive(w) && CanOwnADialog(w) && (skip == null || !skip(w));

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

    private static bool CanOwnADialog(Window window) => window is not ITransientHostWindow;

    /// <summary>
    /// Shows <paramref name="dialog"/> modally, holding its owner open until the dialog is answered.
    /// </summary>
    /// <remarks>
    /// An owned window is destroyed by Win32 along with its owner, and a modal dialog destroyed that way
    /// never gets to finish closing: ShowDialog pumps a nested dispatcher frame until the WPF-level close
    /// runs, and that close is also what re-enables every other window it disabled on the way in. Lose it
    /// and the frame pumps forever with every window still disabled -- the whole app frozen, its windows
    /// refusing even to close, with the dialog that was holding it all gone from the screen.
    ///
    /// So the owner is made to wait. Its Closing is cancelled while the dialog is up, and the close it
    /// asked for is carried out the moment ShowDialog returns. The question stays on screen and gets
    /// answered; the window it was asked from goes away a moment later than it meant to.
    ///
    /// Waiting rather than dismissing the dialog, which was the first thing tried: a question the user
    /// is in the middle of reading should not vanish because the window behind it timed out, lost
    /// focus, or was closed by something that never knew the question was being asked. Dismissing it
    /// also silently answers it -- every caller reads a dialog that closed itself as a cancel.
    ///
    /// Detaching instead (Owner = null, so the dialog simply outlives its owner) is not available: WPF
    /// refuses to reassign Owner while a window is being shown as a dialog.
    ///
    /// A close that ignores cancellation -- the application shutting down -- is not covered, and cannot
    /// be: at that point the process is going away with the dialog and everything else.
    /// </remarks>
    public static void ShowModal(Window dialog)
    {
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
    private static bool IsAlive(Window window) => new WindowInteropHelper(window).Handle != IntPtr.Zero;
}
