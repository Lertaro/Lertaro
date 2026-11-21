using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Windows.Automation;

namespace Lertaro.Plugins.Files.Automation;

/// <summary>
/// Reads and writes Files' current folder through its hidden UI Automation text boxes:
/// CurrentPathGet exposes the active pane's path; CurrentPathSet submits navigation.
/// These controls are defined under "Listary Integration" in Files' NavigationToolbar.xaml.
///
/// Each tab/pane has its own pair. Capture FocusedElement while Files has focus, cache it by HWND,
/// then search successive ancestor subtrees for the nearest control. This preserves the target pane
/// after focus moves to Lertaro. Without an anchor, fall back to searching the whole window.
///
/// The write handler is marked "Superseded by Omnibar" upstream; check this integration when updating Files.
/// </summary>
internal static class UiaPathAccessor
{
    // ConcurrentDictionary, not Dictionary: same cross-thread focus-anchor race as Plugins/OneCommander/Automation/UiaPathAccessor.cs (its internals aren't referenceable from this plugin).
    private static readonly ConcurrentDictionary<IntPtr, AutomationElement> _focusAnchors = new();
    // Keep this cache across focus refreshes: the active-path poller can refresh the anchor concurrently
    // with ExecuteItem, and clearing it there would put the slow UIA tree search back on the click path.
    private static readonly ConcurrentDictionary<IntPtr, AutomationElement> _currentPathSetElements = new();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    /// <summary>
    /// Snapshots the currently-focused UI element for later use, while Files (hwnd) still plausibly holds
    /// it. Only called from window-activation tracking / session-start code, never per-keystroke, so the
    /// cost of a UIA property read here is acceptable.
    /// </summary>
    public static void RefreshFocusAnchor(IntPtr hwnd)
    {
        try
        {
            // Sweep entries whose window is gone so a closed window's UIA COM RCW isn't held for the process lifetime -- same leak fix as Plugins/OneCommander/Automation/UiaPathAccessor.cs.
            foreach (var cached in _focusAnchors)
            {
                if (!IsWindow(cached.Key))
                    _focusAnchors.TryRemove(cached.Key, out _);
            }

            var focused = AutomationElement.FocusedElement;
            if (focused != null)
                _focusAnchors[hwnd] = focused;
        }
        catch
        {
            // Best-effort; a failed snapshot just means the whole-window fallback below kicks in later.
        }
    }

    public static string? GetCurrentPath(IntPtr hwnd)
    {
        // Locate both controls while the host still has focus and retain the write control so ExecuteItem's
        // cross-process UIA search isn't repeated per result -- same reason as Plugins/OneCommander/Automation/UiaPathAccessor.cs.
        var elements = FindNearestPaneElements(hwnd);
        if (elements.SetElement != null)
            _currentPathSetElements[hwnd] = elements.SetElement;

        var element = elements.GetElement;
        if (element == null) return null;
        try
        {
            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj)) return null;
            return ((ValuePattern)patternObj).Current.Value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets every currently materialized Files pane/tab path in a window. Inactive virtualized tabs are
    /// intentionally absent because Files does not expose their paths to UI Automation.
    /// </summary>
    public static IReadOnlyList<string> GetCurrentPaths(IntPtr hwnd)
    {
        var paths = new List<string>();
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, "CurrentPathGet");
            var elements = root.FindAll(TreeScope.Descendants, condition);
            foreach (AutomationElement element in elements)
            {
                if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj)) continue;
                var path = ((ValuePattern)patternObj).Current.Value;
                if (!string.IsNullOrWhiteSpace(path))
                    paths.Add(path);
            }
        }
        catch
        {
            // UI Automation is best-effort; a closed or busy window contributes no paths.
        }
        return paths;
    }

    public static bool SetCurrentPath(IntPtr hwnd, string path)
    {
        var usedCache = _currentPathSetElements.TryGetValue(hwnd, out var element);
        if (!usedCache)
            element = FindNearestPaneElement(hwnd, "CurrentPathSet");
        if (element == null) return false;

        try
        {
            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj))
            {
                if (usedCache)
                {
                    _currentPathSetElements.TryRemove(hwnd, out _);
                    return SetCurrentPath(hwnd, path);
                }

                return false;
            }

            ((ValuePattern)patternObj).SetValue(path);
            return true;
        }
        catch
        {
            if (usedCache)
            {
                // UIA elements can become stale when Files recreates a pane or tab. Remove only this
                // window's cached control and retry the original scoped lookup once.
                _currentPathSetElements.TryRemove(hwnd, out _);
                return SetCurrentPath(hwnd, path);
            }

            return false;
        }
    }

    private static (AutomationElement? GetElement, AutomationElement? SetElement) FindNearestPaneElements(IntPtr hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, "CurrentPathGet"),
                new PropertyCondition(AutomationElement.AutomationIdProperty, "CurrentPathSet"));

            if (_focusAnchors.TryGetValue(hwnd, out var anchor))
            {
                var walker = TreeWalker.RawViewWalker;
                var node = anchor;
                AutomationElement? getElement = null;
                AutomationElement? setElement = null;
                for (var depth = 0; depth < 25 && node != null; depth++)
                {
                    try
                    {
                        var matches = node.FindAll(TreeScope.Subtree, condition);
                        for (var index = 0; index < matches.Count; index++)
                        {
                            var match = matches[index];
                            switch (match.Current.AutomationId)
                            {
                                case "CurrentPathGet":
                                    getElement ??= match;
                                    break;
                                case "CurrentPathSet":
                                    setElement ??= match;
                                    break;
                            }
                        }
                    }
                    catch { }

                    if (getElement != null && setElement != null)
                        return (getElement, setElement);

                    var isRoot = false;
                    try { isRoot = System.Windows.Automation.Automation.Compare(node, root); } catch { }
                    if (isRoot) break;

                    try { node = walker.GetParent(node); } catch { node = null; }
                }
            }

            return (
                root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "CurrentPathGet")),
                root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "CurrentPathSet")));
        }
        catch
        {
            return (null, null);
        }
    }

    private static AutomationElement? FindNearestPaneElement(IntPtr hwnd, string automationId)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);

            if (_focusAnchors.TryGetValue(hwnd, out var anchor))
            {
                if (!IsWindow(hwnd))
                {
                    _focusAnchors.TryRemove(hwnd, out _);
                    anchor = null;
                }

                if (anchor == null)
                {
                    return root.FindFirst(TreeScope.Descendants, condition);
                }

                var walker = TreeWalker.RawViewWalker;
                var node = anchor;
                for (var depth = 0; depth < 25 && node != null; depth++)
                {
                    AutomationElement? found = null;
                    try { found = node.FindFirst(TreeScope.Subtree, condition); } catch { }
                    if (found != null) return found;

                    var isRoot = false;
                    try { isRoot = System.Windows.Automation.Automation.Compare(node, root); } catch { }
                    if (isRoot) break;

                    try { node = walker.GetParent(node); } catch { node = null; }
                }
            }

            // No anchor captured (or it led nowhere) -- fall back to the whole window, matching a
            // single-pane/single-tab Files or a session where we never observed focus in time.
            return root.FindFirst(TreeScope.Descendants, condition);
        }
        catch
        {
            return null;
        }
    }
}
