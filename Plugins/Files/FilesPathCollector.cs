using Lertaro.Plugins.Files.Automation;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Helpers;
namespace Lertaro.Plugins.Files;

/// <summary>
/// Collects the active folder path from a Files (files-community/Files, formerly Files UWP) window outside
/// of an inline-search session (e.g. for "jump to this folder" prompts when a save/open dialog appears).
/// Files is a WinUI3/Windows App SDK app -- its top-level window's native class name is the generic one the
/// Windows App SDK registers for every WinUI3 app on the machine, not something unique to Files, so
/// CanHandle can't gate on class name alone the way Total Commander/Directory Opus (fixed native classes)
/// can -- TryGetPath re-checks the process name (which this method does receive) before paying for a UIA
/// lookup, same pattern OneCommanderPathCollector already uses for the identical WPF-generated-class problem.
/// </summary>
public class FilesPathCollector : IActivePathCollector
{
    public string Name => "Files";
    public string TargetName => "Files";

    // "WinUI" is a loose, best-effort substring match on the Windows App SDK's generic top-level window
    // class (unverified against a live Files install -- not code-searchable from its open-source repo
    // alone, since the class is registered by the WinAppSDK runtime, not Files itself). Shared by every
    // WinUI3 app on the machine either way, so TryGetPath's own process-name check below is the real gate;
    // if the actual class name turns out not to contain "WinUI", this collector simply never triggers
    // (harmless -- core inline search is unaffected, same fallback OneCommanderPathCollector relies on).
    public bool CanHandle(string className) => !string.IsNullOrEmpty(className) && className.Contains("WinUI", StringComparison.OrdinalIgnoreCase);

    public string? TryGetPath(IntPtr activeHwnd, string activeClassName, IntPtr windowHwnd, string windowClassName, string processName)
    {
        if (!processName.Equals("Files", StringComparison.OrdinalIgnoreCase))
            return null;

        var hwnd = windowHwnd != IntPtr.Zero ? windowHwnd : activeHwnd;

        // Called on window-activation tracking, before any inline-search window has had a chance to steal
        // focus away -- the best opportunity to snapshot which pane is actually active (see UiaPathAccessor).
        UiaPathAccessor.RefreshFocusAnchor(hwnd);

        var path = UiaPathAccessor.GetCurrentPath(hwnd);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    /// <summary>
    /// Returns every Files pane/tab currently exposed through UI Automation in each open Files window.
    /// </summary>
    public IReadOnlyList<OpenedFolder> GetOpenedFolders()
    {
        var folders = new List<OpenedFolder>();
        foreach (var window in OpenFolderWindowEnumerator.FindVisibleWindows(IsFilesWindow))
        {
            foreach (var path in UiaPathAccessor.GetCurrentPaths(window))
            {
                if (!string.IsNullOrWhiteSpace(path))
                    folders.Add(new OpenedFolder(path, window));
            }
        }
        return folders;
    }

    private static bool IsFilesWindow(IntPtr window) =>
        OpenFolderWindowEnumerator.GetProcessName(window).Equals("Files", StringComparison.OrdinalIgnoreCase);
}
