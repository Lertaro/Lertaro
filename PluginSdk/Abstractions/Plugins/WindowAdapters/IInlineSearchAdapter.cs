namespace Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

public interface IInlineSearchAdapter : IPluginComponent
{

    /// <summary>
    /// Check if this adapter can handle the given active window.
    /// </summary>
    bool CanHandle(IntPtr hwnd, string className, string processName);

    /// <summary>
    /// Checks whether this adapter recognizes the given window as its host, independently of whether
    /// inline search is currently enabled. Quick Navigation uses this to keep its own opt-in separate
    /// from the inline-search setting.
    /// </summary>
    bool CanRecognizeHost(IntPtr hwnd, string className, string processName) => CanHandle(hwnd, className, processName);

    /// <summary>
    /// Check if the inline search window can be triggered when the specified control has focus.
    /// </summary>
    bool CanTrigger(IntPtr focusedHwnd, string className);

    /// <summary>
    /// Retrieves the current search scope path from the active window.
    /// </summary>
    string? GetSearchScope(IntPtr hwnd);

    /// <summary>
    /// Defines the action when a search result item is clicked or executed.
    /// Returns true if the action was successfully handled, false otherwise.
    /// </summary>
    /// <remarks>
    /// This can run in the host app's elevated Hook process rather than the main App process. Don't call
    /// <c>Directory.Exists</c>/<c>File.Exists</c> on <paramref name="path"/> to tell a folder from a file --
    /// when running elevated (as Lertaro's own built-in adapters can), a mapped network drive letter set
    /// up under the caller's own non-elevated logon session is invisible to that check (UAC's split token
    /// puts the elevated process in a different logon session), so it would silently report a perfectly
    /// valid path as nonexistent. The caller already knows and marks it with a trailing directory separator
    /// (<c>Path.EndsInDirectorySeparator(path)</c>) -- trust that instead.
    /// </remarks>
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);

    /// <summary>
    /// Gets the window bounds for positioning/docking the inline search window.
    /// </summary>
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);

    /// <summary>
    /// Check if the secondary actions menu can be entered for the active window.
    /// </summary>
    bool CanEnterActionsMode(IntPtr hwnd);

    /// <summary>
    /// Indicates whether this adapter targets a file manager / explorer-like window
    /// that supports combining local folder search with global search results.
    /// </summary>
    bool IsFileExplorer => false;

    /// <summary>
    /// Determines whether the Quick Navigation menu may be shown for a mouse click that landed on
    /// <paramref name="hwndUnderCursor"/> (of class <paramref name="classNameUnderCursor"/>) within this
    /// host's window. Defaults to <see cref="CanTrigger"/> -- the same "is this the host's file list"
    /// check inline search already uses for its keyboard hotkey -- since both represent the same question
    /// ("is the user positioned over this host's file list").
    /// </summary>
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => CanTrigger(hwndUnderCursor, classNameUnderCursor);

    /// <summary>
    /// Returns the full list of items currently available in the target control.
    /// When non-empty, the inline search filters this list instead of querying the
    /// global search service. Return an empty enumerable (default) to fall back to
    /// the normal streaming search.
    /// </summary>
    IEnumerable<string> GetListItems(IntPtr hwnd) => Array.Empty<string>();

    /// <summary>
    /// Called when the highlighted result in the inline search window changes,
    /// so the adapter can mirror the selection in the host control in real time.
    /// </summary>
    void OnSelectionChanged(IntPtr hwnd, string path) { }

    /// <summary>
    /// How long (in ms) the search box should wait after a selection-mirror sync before reclaiming its
    /// own keyboard focus. Some hosts' <see cref="OnSelectionChanged"/> implementation ends up activating
    /// the host window itself -- either as a side effect of whatever native command it runs, or because
    /// the host requires being foreground to act on that command at all -- which steals keyboard focus
    /// away from the search box while the user may still be typing. 0 (the default) means this adapter's
    /// OnSelectionChanged never does that, so no reclaim is scheduled. Adapters that do need it tune their
    /// own value here rather than sharing one process-wide constant, since how long the steal takes (and
    /// whether it happens at all) depends entirely on that specific host's own behavior.
    /// </summary>
    int SelectionSyncFocusReclaimDelayMs => 0;

    /// <summary>
    /// Called when the search session ends.
    /// <paramref name="executed"/> is true when the user confirmed a result,
    /// false when the session was cancelled or the window was closed.
    /// </summary>
    void OnSearchFinished(IntPtr hwnd, bool executed) { }
}
