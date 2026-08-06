using System.IO;
using Lertaro.Plugins.TotalCommander.Win32;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.TotalCommander;

/// <summary>
/// Collects the active (source) pane's directory from a Total Commander window by asking TC directly over its
/// documented WM_COPYDATA remote-control interface, so it works regardless of TC's (custom-drawn) UI layout.
/// </summary>
public class TotalCommanderPathCollector : IActivePathCollector
{
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
        var path = Win32Helper.QuerySourcePanelPath(main);
        if (string.IsNullOrEmpty(path)) return null;

        // TC returns paths with a trailing backslash (e.g. "C:\Users\"); keep it valid but normalized.
        if (path.Length > 3 && path.EndsWith('\\'))
            path = path.TrimEnd('\\');
        return Directory.Exists(path) ? path : null;
    }
}
