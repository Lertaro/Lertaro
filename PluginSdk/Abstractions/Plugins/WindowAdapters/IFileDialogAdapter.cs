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
