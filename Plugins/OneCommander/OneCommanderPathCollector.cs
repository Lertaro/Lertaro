using Lertaro.Plugins.OneCommander.Automation;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.OneCommander;

/// <summary>
/// Collects the active folder path from a OneCommander window outside of an inline-search session (e.g.
/// for "jump to this folder" prompts when a save/open dialog appears). Unlike Directory Opus / Total
/// Commander, OneCommander's top-level window class name is generated at runtime by WPF rather than a
/// fixed native class, so CanHandle can't check for an exact literal class name -- WPF's default class name
/// embeds the hosting executable's name (e.g. "HwndWrapper[OneCommander.exe;;&lt;guid&gt;]"), so this matches on
/// that substring instead. Narrow enough that it won't misfire on other WPF apps in practice, but not
/// verified against a live OneCommander install; if a build customizes its window class this simply never
/// matches (harmless -- this collector just never triggers, core inline search is unaffected).
/// </summary>
public class OneCommanderPathCollector : IActivePathCollector
{
    public string Name => "OneCommander";
    public string TargetName => "OneCommander";

    public bool CanHandle(string className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        return className.Contains("OneCommander", StringComparison.OrdinalIgnoreCase);
    }

    public string? TryGetPath(IntPtr activeHwnd, string activeClassName, IntPtr windowHwnd, string windowClassName, string processName)
    {
        // CanHandle only sees the (heuristic, WPF-generated) class name; confirm via process name -- which
        // this method does receive -- before paying for a UIA lookup.
        if (!processName.Equals("OneCommander", StringComparison.OrdinalIgnoreCase))
            return null;

        var hwnd = windowHwnd != IntPtr.Zero ? windowHwnd : activeHwnd;

        // Called on window-activation tracking, before any inline-search window has had a chance to steal
        // focus away -- the best opportunity to snapshot which pane is actually active (see UiaPathAccessor).
        UiaPathAccessor.RefreshFocusAnchor(hwnd);

        var path = UiaPathAccessor.GetCurrentPath(hwnd);
        return PathValidation.LooksLikeRootedPath(path) ? path : null;
    }
}
