using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Windows.Automation;

namespace Lertaro.Plugins.Files.Automation;

/// <summary>
/// Reads/writes Files' current-folder path via UI Automation. Files (files-community/Files, formerly Files
/// UWP) has no window-message protocol, but ships a hidden pair of zero-size TextBoxes purpose-built for
/// this exact scenario -- labeled "Listary Integration" in its own source
/// (UserControls/NavigationToolbar.xaml): AutomationId "CurrentPathGet" (read-only, bound to the active
/// pane's working directory) and "CurrentPathSet" (writing to it raises a query-submitted navigation, same
/// as typing into the address bar and pressing Enter). Note the write path is wired through a handler
/// marked [Obsolete("Superseded by Omnibar.")] in Files' own source as of this writing -- still functional,
/// but Files' newer Omnibar address control may eventually drop it; watch for SetCurrentPath silently
/// stopping to work on a future Files version.
///
/// Files supports multiple tabs and a dual-pane layout, and (per the same pattern OneCommander's own plugin
/// already had to solve) each pane/tab exposes its own "CurrentPathGet"/"CurrentPathSet" pair -- searching
/// the whole window blindly always finds the same (first) one regardless of which is actually active.
///
/// To target the right pane: every time Files plausibly still holds OS focus (from FilesPathCollector and
/// FilesInlineSearchAdapter.GetSearchScope, both called before the inline search window steals focus away),
/// we snapshot AutomationElement.FocusedElement and cache it per window handle. Later calls (notably
/// ExecuteItem, which only runs after focus has already moved to our own window) walk UP from that cached
/// anchor, searching each ancestor's subtree for the target automation ID, so the *nearest* pane's controls
/// win over some other, inactive pane's. If no anchor was ever captured, this falls back to a whole-window
/// search (the original behavior).
/// </summary>
internal static class UiaPathAccessor
{
    // ConcurrentDictionary, not Dictionary: RefreshFocusAnchor writes from the Hook's tracker thread
    // (WinEvent-driven focus tracking) while GetCurrentPath/SetCurrentPath can now read from whatever
    // thread is running an IInlineSearchAdapter call -- each ExecuteItem/etc. gets its own dedicated STA
    // thread (see InlineAdapterCommandHandler.RunOnSta), so a read here can genuinely race a write.
    private static readonly ConcurrentDictionary<IntPtr, AutomationElement> _focusAnchors = new();

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
        var element = FindNearestPaneElement(hwnd, "CurrentPathGet");
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
        var element = FindNearestPaneElement(hwnd, "CurrentPathSet");
        if (element == null) return false;
        try
        {
            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj)) return false;
            ((ValuePattern)patternObj).SetValue(path);
            return true;
        }
        catch
        {
            return false;
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
