using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.Plugins.WPS.Interop;

namespace Lertaro.Plugins.WPS;

/// <summary>
/// File-dialog integration for WPS Office's Open/Save dialog, so Lertaro's own path picker can drive it
/// the way it already drives Explorer's common dialogs (see CoreExtensions' ClassicFileDialogAdapter).
/// </summary>
/// <remarks>
/// WPS does not use the Windows common dialog. It ships its own, built on Qt, which is why this adapter
/// looks nothing like the others: Qt paints its widgets inside one native window instead of giving each a
/// HWND, so there are no child windows to walk and everything inside goes through UI Automation
/// (<see cref="WPSDialogAutomation"/>). Nothing here reads caption or label text, since WPS is localized.
/// </remarks>
public class WPSFileDialogAdapter : IFileDialogAdapter
{
    /// <summary>
    /// How long to wait for the dialog to actually reach the foreground before committing the path.
    /// </summary>
    /// <remarks>
    /// Enter is synthesised at the system level and lands wherever focus is when it is processed, so
    /// giving up here is the correct outcome: better to report failure than to type a path into whatever
    /// window happens to be in front.
    /// </remarks>
    private const int ActivationTimeoutMs = 1000;

    public string Name => "WPS";

    /// <summary>
    /// The path goes into the dialog's file-name box, which takes a folder or a file exactly as an
    /// Open/Save dialog's does, so callers should keep passing whichever the user picked -- hence the
    /// default false rather than the folder-only behaviour the archive-tool adapters opt into.
    /// </summary>
    public bool TargetIsFolderOnly => false;

    /// <summary>
    /// Two free string comparisons, then the one call that costs something.
    /// </summary>
    /// <remarks>
    /// Order matters. Only GetDialog can actually identify the dialog, but it is a cross-process UI
    /// Automation call that can block until that layer's own timeout if WPS is busy, and this runs
    /// whenever the foreground changes -- including while a dialog is being destroyed. The process name
    /// rejects every window on the machine that is not WPS's, and the class name rejects WPS's own main
    /// window and frame, so in practice nothing but the dialog itself ever reaches the third test.
    /// </remarks>
    public bool CanHandle(IntPtr hwnd, string className, string processName)
        => WPSDialogIdentity.IsWPSProcess(processName)
            && WPSDialogIdentity.CouldBeDialogWindowClass(className)
            && WPSDialogAutomation.GetDialog(hwnd) != null;

    /// <summary>
    /// Always null: this dialog does not report the folder it is showing.
    /// </summary>
    /// <remarks>
    /// Not an omission. The dialog has no control that reliably holds the current folder as text across
    /// WPS versions -- the file-name box holds what the user typed, not where the view is -- and Listary's
    /// own WPS plugin declares the same by setting IsOpenedFolderProvider to false. Returning null leaves
    /// SearchScope at whatever it was rather than feeding it a guess, and it keeps this method free: it is
    /// the one member here that ExplorerActivePathPoller calls on every single tick while the dialog is
    /// active, so reaching into UI Automation from it would put a cross-process call on a timer.
    /// </remarks>
    public string? GetCurrentPath(IntPtr hwnd) => null;

    /// <summary>
    /// Puts the path in the file-name box and commits it, which is how this dialog is navigated: typing a
    /// folder and pressing Enter moves the view into it, and typing a file opens it.
    /// </summary>
    public bool NavigateTo(IntPtr hwnd, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return false;

        var dialog = WPSDialogAutomation.GetDialog(hwnd);
        if (dialog == null)
            return false;

        var editor = WPSDialogAutomation.FindFileNameEditor(dialog, hwnd);
        if (editor == null)
            return false;

        if (!WPSDialogAutomation.TrySetValue(editor, targetPath))
            return false;

        // Foreground before the keystroke, and confirmed rather than assumed: SendEnter goes wherever
        // focus is when it is processed. Focus is then put back on the editor because activating the
        // window restores it to whichever control the dialog last had, which need not be this one.
        if (!WPSWindowInterop.ActivateAndWait(hwnd, ActivationTimeoutMs))
            return false;

        WPSDialogAutomation.TryFocus(dialog);
        if (!WPSDialogAutomation.TryFocus(editor))
            return false;

        // Nothing after the keystroke. An earlier version spent up to a second here polling the dialog to
        // put the user's half-typed file name back afterwards, on the assumption that committing a path
        // through the file-name box would destroy it. Probing a live dialog showed WPS restores the name
        // itself once it has navigated, so that loop bought nothing -- while pouring cross-process
        // automation calls into a dialog that, when the committed path was a file, is in the middle of
        // closing.
        return WPSWindowInterop.SendEnter();
    }

    /// <summary>
    /// The whole dialog's bounds, matching what every other dialog adapter reports: the host rejects a
    /// dock rect under 100px tall as "not a real target" (InlineSearchWindowPositioner.PositionWindowCore)
    /// and would silently fall back to a fixed screen position if this returned the file-name row alone.
    /// Where that row actually is -- which is what the card lines itself up with horizontally -- is a
    /// separate answer, in <see cref="TryGetTargetFieldBounds"/>.
    /// </summary>
    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;

        // Win32 only, like every other adapter's version of this. It is asked for the rect of a window
        // the host has already accepted as the dialog, so re-identifying it here bought nothing -- and it
        // bought that nothing with a cross-process automation call on a path that keeps running while the
        // dialog is being destroyed. TryGetDialogRect fails on a dead window by itself, which is the only
        // thing this needs to notice.
        if (!WPSWindowInterop.TryGetDialogRect(hwnd, out var r))
            return false;

        rect = new AdapterRect { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
        return true;
    }

    /// <summary>
    /// Where the file-name box actually sits inside that dialog, so the card can hang under it rather than
    /// under the middle of a dialog whose box starts a third of the way in.
    /// </summary>
    /// <remarks>
    /// The host asks this on the positioning path, which runs again every time the dialog moves. Only the
    /// first answer per dialog size costs anything: WPS lays its widgets out relative to the dialog's own
    /// edges, so a dialog that has merely been dragged somewhere keeps its box at the same offset inside
    /// itself, and that offset is translated instead of re-measured. A dialog that changed size is a
    /// different matter -- there the layout really did change -- and gets exactly one fresh, non-retrying
    /// UI Automation attempt, because spending <see cref="WPSDialogAutomation.EditorLookupTimeoutMs"/>
    /// sleeping on this path would put a cross-process wait on the UI thread. Failing that attempt returns
    /// false, which is the honest answer and costs the user nothing but the old centered placement.
    /// </remarks>
    public bool TryGetTargetFieldBounds(IntPtr hwnd, out AdapterRect bounds)
    {
        bounds = default;
        if (!WPSWindowInterop.TryGetDialogRect(hwnd, out var dialogRect))
            return false;

        var dialog = new AdapterRect { Left = dialogRect.Left, Top = dialogRect.Top, Right = dialogRect.Right, Bottom = dialogRect.Bottom };
        if (hwnd == _anchoredHwnd && SameSize(dialog, _anchoredDialog))
        {
            bounds = Translate(_anchoredField, _anchoredDialog, dialog);
            return true;
        }

        var measured = WPSDialogAutomation.TryGetFileNameEditorBounds(hwnd);
        if (measured == null)
            return false;

        // UI Automation reports physical screen pixels, the same space TryGetDialogRect answers in for a
        // DPI-aware client, so nothing here is scaled -- and a Qt window makes DWM's extended-frame call
        // fail with E_INVALIDARG, which means both of these really do come from GetWindowRect's system.
        _anchoredHwnd = hwnd;
        _anchoredDialog = dialog;
        _anchoredField = new AdapterRect
        {
            Left = (int)measured.Value.Left,
            Top = (int)measured.Value.Top,
            Right = (int)measured.Value.Right,
            Bottom = (int)measured.Value.Bottom,
        };
        bounds = _anchoredField;
        return true;
    }

    // The dialog rect the cached field was measured against. _anchoredHwnd stays Zero until a measurement
    // exists, and no live window is Zero, so that alone marks the cache as empty.
    private IntPtr _anchoredHwnd;
    private AdapterRect _anchoredDialog;
    private AdapterRect _anchoredField;

    internal static bool SameSize(AdapterRect a, AdapterRect b) =>
        a.Right - a.Left == b.Right - b.Left && a.Bottom - a.Top == b.Bottom - b.Top;

    /// <summary>
    /// <paramref name="field"/> re-expressed for a window that has travelled from
    /// <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    internal static AdapterRect Translate(AdapterRect field, AdapterRect from, AdapterRect to)
    {
        var dx = to.Left - from.Left;
        var dy = to.Top - from.Top;
        return new AdapterRect
        {
            Left = field.Left + dx,
            Top = field.Top + dy,
            Right = field.Right + dx,
            Bottom = field.Bottom + dy,
        };
    }

    /// <summary>
    /// Hands focus back to the file-name box. No AttachThreadInput dance like the Win32 adapters need --
    /// there is no control HWND to SetFocus on, and UI Automation's own SetFocus crosses the process and
    /// thread boundary itself.
    /// </summary>
    public bool RestoreFocus(IntPtr hwnd) =>
        // Nothing but a liveness check and SetForegroundWindow.
        //
        // This is the one thing the adapter does that WRITES to the dialog while it may be closing: the
        // App sends RestoreDialogFocus as the inline window hands focus back, and cancelling the dialog
        // does both at once. Worse, the App first calls AllowSetForegroundWindow so the elevated hook can
        // take the foreground unconditionally -- so a dialog that is already being destroyed gets the
        // foreground forced onto it, and the desktop is left pointing at a window that no longer exists.
        //
        // The visibility check is what makes that unlikely: a dismissed dialog is hidden before it is
        // destroyed. Reaching further in to put the caret back in the file-name box, which is what this
        // used to do through UI Automation, is not worth any of that -- focus landing on the dialog
        // rather than inside its text box is a detail nobody will notice.
        WPSWindowInterop.IsLiveAndVisible(hwnd) && WPSWindowInterop.Activate(hwnd);

}
