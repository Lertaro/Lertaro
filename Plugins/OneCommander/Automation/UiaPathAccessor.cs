using System.Windows.Automation;

namespace Lertaro.Plugins.OneCommander.Automation;

/// <summary>
/// Reads/writes OneCommander's current-folder path via UI Automation. The top-level window exposes a single
/// "CurrentPathGet"/"CurrentPathSet" pair as direct children that mirror the active pane/tab; inactive tabs
/// expose no path of their own, so one window can only contribute its active folder.
/// </summary>
/// <remarks>
/// These are located with <see cref="TreeWalker"/> instead of FindFirst/FindAll on purpose: Find* searches the
/// control view, and current .NET WPF hosts (OneCommander 3.108 on Windows 11 26H2) report every element as
/// IsControlElement=false to a cross-process client, so Find* silently returns nothing while the raw-view walk
/// and the ValuePattern below keep working. .NET Framework WPF hosts do not show this.
/// </remarks>
internal static class UiaPathAccessor
{
    private const string GetPathAutomationId = "CurrentPathGet";
    private const string SetPathAutomationId = "CurrentPathSet";

    public static string? GetCurrentPath(IntPtr hwnd)
    {
        var element = FindPathElements(hwnd).GetElement;
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
    /// Gets the paths this window exposes, which is the active pane/tab only -- OneCommander publishes no path
    /// for the tabs that are not displayed.
    /// </summary>
    public static IReadOnlyList<string> GetCurrentPaths(IntPtr hwnd)
    {
        var paths = new List<string>();
        foreach (var child in TopLevelChildren(hwnd))
        {
            try
            {
                if (child.Current.AutomationId != GetPathAutomationId) continue;
                if (!child.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj)) continue;
                var path = ((ValuePattern)patternObj).Current.Value;
                if (!string.IsNullOrWhiteSpace(path))
                    paths.Add(path);
            }
            catch
            {
                // Skip an element whose provider went away mid-walk.
            }
        }
        return paths;
    }

    public static bool SetCurrentPath(IntPtr hwnd, string path)
    {
        var element = FindPathElements(hwnd).SetElement;
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

    private static (AutomationElement? GetElement, AutomationElement? SetElement) FindPathElements(IntPtr hwnd)
    {
        AutomationElement? getElement = null;
        AutomationElement? setElement = null;
        foreach (var child in TopLevelChildren(hwnd))
        {
            string automationId;
            try { automationId = child.Current.AutomationId; } catch { continue; }

            switch (automationId)
            {
                case GetPathAutomationId:
                    getElement ??= child;
                    break;
                case SetPathAutomationId:
                    setElement ??= child;
                    break;
            }

            if (getElement != null && setElement != null)
                break;
        }

        return (getElement, setElement);
    }

    private static List<AutomationElement> TopLevelChildren(IntPtr hwnd)
    {
        var children = new List<AutomationElement>();
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var walker = TreeWalker.RawViewWalker;
            var child = walker.GetFirstChild(root);
            while (child != null)
            {
                children.Add(child);
                child = walker.GetNextSibling(child);
            }
        }
        catch
        {
            // A window that is gone (or whose provider is wedged) has no children to offer.
        }
        return children;
    }
}
