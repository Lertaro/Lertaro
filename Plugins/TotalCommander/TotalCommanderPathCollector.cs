using System.IO;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Helpers;
using Lertaro.Plugins.TotalCommander.Win32;
namespace Lertaro.Plugins.TotalCommander;

/// <summary>
/// Collects the active source-pane path through the adapter's editor-safe scope reader. This remains necessary
/// when inline search is disabled: querying TC while its path editor is active cancels that edit.
/// </summary>
public class TotalCommanderPathCollector : IActivePathCollector
{
    private readonly TotalCommanderInlineSearchAdapter _scopeReader = new();

    public string Name => "Total Commander";
    public string TargetName => "Total Commander";

    public bool CanHandle(string className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        return className.Equals("TTOTAL_CMD", StringComparison.OrdinalIgnoreCase);
    }

    public string? TryGetPath(IntPtr activeHwnd, string activeClassName, IntPtr windowHwnd, string windowClassName, string processName)
    {
        var main = windowHwnd != IntPtr.Zero ? windowHwnd : activeHwnd;
        var path = _scopeReader.GetSearchScope(main);
        if (string.IsNullOrEmpty(path)) return null;

        return Directory.Exists(path) ? path : null;
    }

    /// <summary>
    /// Returns both panels from every open Total Commander window through its documented protocol.
    /// </summary>
    public IReadOnlyList<OpenedFolder> GetOpenedFolders()
    {
        var folders = new List<OpenedFolder>();
        foreach (var window in OpenFolderWindowEnumerator.FindVisibleWindows(IsTotalCommanderWindow))
        {
            AddFolder(folders, _scopeReader.GetSearchScope(window), window);
            AddFolder(folders, Win32Helper.QueryTargetPanelPath(window), window);
        }
        return folders;
    }

    private static bool IsTotalCommanderWindow(IntPtr window) =>
        Win32Helper.GetClassName(window).Equals("TTOTAL_CMD", StringComparison.OrdinalIgnoreCase);

    private static void AddFolder(List<OpenedFolder> folders, string? path, IntPtr window)
    {
        if (!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path))
            folders.Add(new OpenedFolder(path, window));
    }
}
