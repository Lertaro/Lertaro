using Lertaro.Core.Hook;

namespace Lertaro.Core.Hook.InlineSearch;

/// <summary>
/// The one answer to "which folder should this go to, or work from": the folder the user was last working in,
/// in a file manager rather than a dialog shell.
/// </summary>
/// <remarks>
/// Kept in one place because the priority order and the "the desktop is not a file manager" rule used to be
/// re-derived at each call site (Ctrl+G in the Hook, the inline card's baseline in the App) and drifted: the
/// desktop kept leaking in as a destination, and each fix missed a different route.
/// </remarks>
public static class QuickSwitchTargetResolver
{
    /// <summary>
    /// The folder to switch to, resolved with the one priority order: the dialog-time memory first, then the
    /// file manager that was last activated (see <see cref="FromOpenFolders"/>, which the Hook can answer).
    /// </summary>
    public static string? Resolve(ExplorerTracker tracker) => RememberedPath(tracker) ?? FromOpenFolders();

    /// <summary>
    /// The same thing for a caller that only has the paths the Hook already published (the App process, where the
    /// collectors do not run): those arrive ordered by last-activation and with desktop sources filtered out.
    /// </summary>
    public static string? Resolve(ExplorerTracker tracker, IReadOnlyList<string>? publishedPaths)
        => RememberedPath(tracker)
           ?? (publishedPaths ?? Array.Empty<string>()).FirstOrDefault(IsWorthSwitchingTo);

    /// <summary>The dialog-time memory -- the folder the file manager showed when the dialog opened -- or null.</summary>
    /// <remarks>
    /// Clicking the desktop fills that memory too, and the desktop is not a file manager, so the value is judged
    /// from the WINDOW that provided it (never by comparing paths): a file manager window parked on the desktop
    /// folder is a normal window and still counts.
    /// </remarks>
    public static string? RememberedPath(ExplorerTracker tracker)
    {
        // The window that provided the path is the exact test. The App mirrors a flag rather than the handle, so
        // there it falls back to the origin flag the Hook publishes WITH the path (PathCaptured's isDesktop):
        // that keeps both sides on the same rule instead of one of them going blind. Note this is the origin of
        // the remembered path, never "is the desktop the active window right now" -- by the time Ctrl+G arrives
        // the active window is the dialog.
        var fromDesktop = tracker.LastPathProviderHwnd != IntPtr.Zero
            ? IsDesktopWindow(tracker.LastPathProviderHwnd)
            : tracker.IsDesktop;
        if (fromDesktop)
            return null;

        var path = tracker.LastActiveExplorerPath;
        return IsWorthSwitchingTo(path) ? path : null;
    }

    /// <summary>The file manager that was last activated, from the open-folder collectors, or null.</summary>
    /// <remarks>
    /// Z-order is the shell's own notion of "most recently used" and needs no activation history. This walks the
    /// collectors' snapshots, which is COM (ShellWindows) work: call it off the keyboard hook thread.
    /// </remarks>
    public static string? FromOpenFolders()
    {
        foreach (var folder in PluginSdk.Registries.OpenedFolderCollectorRegistry.GetOpenedFolders()
                     .Where(folder => IsWorthSwitchingTo(folder.Path) && IsFileManagerWindow(folder.WindowHandle))
                     .OrderBy(folder => ExplorerNativeHooks.ZOrderRank(folder.WindowHandle)))
        {
            return folder.Path;
        }

        return null;
    }

    /// <summary>Whether a path is a real folder this feature can be sent to.</summary>
    internal static bool IsWorthSwitchingTo(string? path)
        => !string.IsNullOrEmpty(path) && Path.IsPathRooted(path) && Directory.Exists(path);

    /// <summary>
    /// Whether a collector entry came from a real file-manager window: a live window that is not the desktop.
    /// </summary>
    /// <remarks>
    /// A missing handle is treated as "not a file manager" on purpose. The desktop is reported by collectors as a
    /// plain Explorer window, and some of them report it with no handle at all, which defeated both the desktop
    /// tests (they answer from a window) and left "the desktop" looking like a folder the user was working in.
    /// A real file manager window always has a handle, so requiring one costs nothing and cannot pick the desktop.
    /// </remarks>
    internal static bool IsFileManagerWindow(IntPtr hwnd)
        => hwnd != IntPtr.Zero && ExplorerNativeHooks.IsWindow(hwnd) && !IsDesktopWindow(hwnd);

    /// <summary>
    /// The desktop's own window, or one of its children -- as opposed to a file manager's window.
    /// </summary>
    /// <remarks>
    /// Delegates to the classifier's own test rather than repeating it: that one already knows the shell window,
    /// the root-owner walk and the Progman/WorkerW names, and it is what decides "the desktop is active" for
    /// every other feature, so the two can never disagree.
    /// </remarks>
    internal static bool IsDesktopWindow(IntPtr hwnd)
        => ExplorerNativeHooks.IsDesktopWindow(hwnd, out _);
}
