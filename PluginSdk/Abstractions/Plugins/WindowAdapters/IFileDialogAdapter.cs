namespace Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

public interface IFileDialogAdapter : IPluginComponent
{

    /// <summary>
    /// Check if this adapter can handle the given active window.
    /// </summary>
    bool CanHandle(IntPtr hwnd, string className, string processName);

    /// <summary>
    /// Retrieves the current folder path from the active dialog window.
    /// </summary>
    string? GetCurrentPath(IntPtr hwnd);

    /// <summary>
    /// Navigates the dialog window to the target directory.
    /// </summary>
    bool NavigateTo(IntPtr hwnd, string targetPath);

    /// <summary>
    /// True for a dialog whose target field can only ever hold a folder (e.g. an archive tool's "extract
    /// to" destination) -- never a specific file, unlike an Open/Save dialog's filename box. Callers that
    /// resolve a picked search result to a target path (see InlineSearchNavigator.RunFallbackChain) use
    /// this to decide whether a picked FILE needs to be resolved to its containing folder first: doing
    /// that resolution here, once, in the interactive process that can actually see the user's own network
    /// drive mappings, is more reliable than leaving each such adapter to re-derive it itself via
    /// File.Exists inside the elevated Hook process, where a drive the user mapped without elevation may
    /// not resolve at all. Defaults false so existing Open/Save-style adapters keep receiving the exact
    /// path they always have.
    /// </summary>
    bool TargetIsFolderOnly => false;

    /// <summary>
    /// Whether Quick Navigation should trigger for a middle-click at the given point inside this dialog.
    /// Default true once <see cref="CanHandle"/> has already matched the dialog itself: unlike a full
    /// file-manager window, a common dialog has no "click a toolbar/breadcrumb button" action for a
    /// middle-click to collide with, so no extra child-control probe is needed unless a specific adapter
    /// wants one -- mirrors <see cref="IInlineSearchAdapter.CanShowQuickNav"/>'s same default-to-CanTrigger
    /// reasoning.
    /// </summary>
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => true;

    /// <summary>
    /// Gets the window bounds for docking the inline search window.
    /// </summary>
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);

    /// <summary>
    /// The screen bounds of the field the card's text actually lands in -- the dialog's file-name box --
    /// for a dialog that can see it.
    /// </summary>
    /// <remarks>
    /// <see cref="GetDockBounds"/> decides how wide the card may be and which edge it hangs from; this says
    /// which part of that window the user is looking at. They are the same thing for a dialog whose target
    /// field spans the window, but not for one like WPS's, whose dock rect is the whole 960px-wide dialog
    /// while the file-name box only starts a third of the way in: centering the card on the dialog then
    /// parks its left edge well to the left of the box. Default false leaves the host's own horizontal
    /// choice in place, which is what every dialog that does not opt in gets.
    ///
    /// Asked on the positioning path, which runs again every time the dialog moves or resizes, so an
    /// implementation is expected to keep it cheap and never block.
    /// </remarks>
    bool TryGetTargetFieldBounds(IntPtr hwnd, out AdapterRect bounds)
    {
        bounds = default;
        return false;
    }

    /// <summary>
    /// The dialog's own file list, for a dialog that can see it.
    /// </summary>
    /// <remarks>
    /// Where the card hangs from when there is NO room under the dialog: the card's visible top-right corner
    /// meets this rect's top-right, which is the same edge the card uses over a file manager's window (whose
    /// dock rect already is its file list). Answering false leaves the card attached to the dialog's own top
    /// edge, covering its address bar and navigation pane.
    /// </remarks>
    bool TryGetFileListBounds(IntPtr hwnd, out AdapterRect bounds)
    {
        bounds = default;
        return false;
    }

    /// <summary>
    /// Restores focus to the appropriate control in the dialog window.
    /// </summary>
    bool RestoreFocus(IntPtr hwnd);
}

public struct AdapterRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
