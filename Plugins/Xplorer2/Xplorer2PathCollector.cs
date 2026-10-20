using System.IO;
using Lertaro.PluginSdk.Helpers;
using Lertaro.Plugins.Xplorer2.Win32;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.Xplorer2;

/// <summary>
/// Collects the folder xplorer2's active pane is showing by reading its address bar over WM_GETTEXT
/// (see Win32Helper). xplorer2 is a C++/ATL application with no remote scripting interface, so this is a
/// window read rather than the script round trip the XYplorer adapter uses.
/// </summary>
public class Xplorer2PathCollector : IActivePathCollector
{
    public string Name => "xplorer2";
    public string TargetName => "xplorer2";

    // "ATL:ExplorerFrame" is the frame class xplorer2 registers itself (found verbatim in its binary, next
    // to ATL:ScrapFrame / ATL:DirStatFrame / ATL:CmdFrame), not the generic "ATL:%p" class ATL generates for
    // unnamed frames -- so it does not collide with arbitrary ATL software. The process-name re-check below
    // is kept anyway, matching the XYplorer collector's defence against a shared generic class.
    public bool CanHandle(string className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        return className.Equals(WindowProbe.FrameClass, StringComparison.OrdinalIgnoreCase);
    }

    public string? TryGetPath(IntPtr activeHwnd, string activeClassName, IntPtr windowHwnd, string windowClassName, string processName)
    {
        if (string.IsNullOrEmpty(processName) || !processName.StartsWith("xplorer2", StringComparison.OrdinalIgnoreCase))
            return null;

        var main = windowHwnd != IntPtr.Zero ? windowHwnd : activeHwnd;
        return Win32Helper.QueryActivePath(main);
    }

    /// <summary>
    /// One folder per xplorer2 window: the active pane of each visible frame.
    /// </summary>
    /// <remarks>
    /// Not every open tab, which is what the XYplorer and Directory Opus collectors report. Measured, and
    /// deliberately accepted rather than worked around:
    ///   - xplorer2 keeps inactive tabs unloaded ("smart tabs"; its own help: "Inactive tabs are hidden from
    ///     view"), so a tab that is not displayed has no window to read at all.
    ///   - Its tab strip is a custom control: TCM_GETITEMCOUNT returns 0 on it, so even the tab *labels* are
    ///     unreachable, let alone their folders.
    ///   - The only folders it exposes as a list are the address bar's per-pane browse history, which is not
    ///     the set of open tabs.
    /// Posting command 33038 ("Switch to next view tab") does move between tabs, so a tab-by-tab sweep is
    /// technically possible -- but it would rewrite which folder the user is looking at on every snapshot,
    /// and the active-path poller takes snapshots continuously. That cost is not worth one extra row.
    /// </remarks>
    public IReadOnlyList<OpenedFolder> GetOpenedFolders()
    {
        var folders = new List<OpenedFolder>();
        foreach (var window in OpenFolderWindowEnumerator.FindVisibleWindows(IsXplorer2Window))
        {
            var path = Win32Helper.QueryActivePath(window);
            if (!string.IsNullOrEmpty(path) && Path.IsPathRooted(path))
                folders.Add(new OpenedFolder(path, window));
        }
        return folders;
    }

    private static bool IsXplorer2Window(IntPtr window) =>
        WindowProbe.GetClassName(window).Equals(WindowProbe.FrameClass, StringComparison.OrdinalIgnoreCase) &&
        OpenFolderWindowEnumerator.GetProcessName(window).StartsWith("xplorer2", StringComparison.OrdinalIgnoreCase);
}
