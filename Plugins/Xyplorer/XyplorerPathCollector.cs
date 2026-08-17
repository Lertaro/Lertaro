using System.IO;
using Lertaro.PluginSdk.Helpers;
using Lertaro.Plugins.Xyplorer.Win32;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.Xyplorer;

/// <summary>
/// Collects the active pane's directory from an XYplorer window by asking XYplorer directly over its
/// documented WM_COPYDATA remote-control interface (see Win32Helper), so it works regardless of XYplorer's
/// (custom-drawn) UI layout.
/// </summary>
public class XyplorerPathCollector : IActivePathCollector
{
    public string Name => "XYplorer";
    public string TargetName => "XYplorer";

    // "ThunderRT6FormDC" is the generic main-form class of any VB6 application, not unique to XYplorer -- the
    // process-name re-check inside TryGetPath is what actually filters out unrelated VB6 apps that happen to
    // share it. Sending a stray script to one of those is harmless: it doesn't understand XYplorer script
    // syntax and just never replies, so TryGetPath falls through to null.
    public bool CanHandle(string className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        return className.Equals("ThunderRT6FormDC", StringComparison.OrdinalIgnoreCase);
    }

    public string? TryGetPath(IntPtr activeHwnd, string activeClassName, IntPtr windowHwnd, string windowClassName, string processName)
    {
        if (string.IsNullOrEmpty(processName) || !processName.StartsWith("xyplorer", StringComparison.OrdinalIgnoreCase))
            return null;

        var main = windowHwnd != IntPtr.Zero ? windowHwnd : activeHwnd;
        var path = Win32Helper.QueryCurrentPath(main);
        if (string.IsNullOrEmpty(path)) return null;

        if (path.Length > 3 && path.EndsWith('\\'))
            path = path.TrimEnd('\\');
        return Directory.Exists(path) ? path : null;
    }

    /// <summary>
    /// Returns every open tab from both panes in each XYplorer window through its documented script API.
    /// </summary>
    public IReadOnlyList<OpenedFolder> GetOpenedFolders()
    {
        var folders = new List<OpenedFolder>();
        foreach (var window in OpenFolderWindowEnumerator.FindVisibleWindows(IsXyplorerWindow))
        {
            foreach (var path in Win32Helper.QueryOpenTabPaths(window))
            {
                if (Path.IsPathRooted(path))
                    folders.Add(new OpenedFolder(path, window));
            }
        }
        return folders;
    }

    private static bool IsXyplorerWindow(IntPtr window) =>
        Win32Helper.GetClassName(window).Equals("ThunderRT6FormDC", StringComparison.OrdinalIgnoreCase) &&
        OpenFolderWindowEnumerator.GetProcessName(window).StartsWith("xyplorer", StringComparison.OrdinalIgnoreCase);
}
