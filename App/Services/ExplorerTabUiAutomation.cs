using System.Windows.Automation;
using Lertaro.Core;

namespace Lertaro.App.Services;

/// <summary>
/// Asks an already-open Explorer window for a new tab through the UI Automation API.
/// </summary>
/// <remarks>
/// Explorer has no public API for its tabs, and the two ways around that are not equal. The one used here
/// is UI Automation: the accessibility API Explorer itself implements and keeps working, because
/// screen readers depend on it. The one it replaces was an undocumented <c>WM_COMMAND 0xA21B</c> posted at
/// the tab window, which is a message number copied out of someone else's reverse engineering and is free
/// to change or be ignored in any Windows update.
///
/// The button is identified by its AutomationId, never by its name: the name is localized ("New tab",
/// "添加新标签页", ...), while the AutomationId is <c>AddButton</c> in the tab strip's WinUI TabView on
/// every locale. Every step is best-effort -- a build without tabs, a hung Explorer, or a future layout
/// that renames the button all end in "no tab was created" and the caller opens the folder the normal way
/// instead.
/// </remarks>
internal static class ExplorerTabUiAutomation
{
    private const string NewTabButtonId = "AddButton";
    private const string TabViewClassName = "Microsoft.UI.Xaml.Controls.TabView";

    /// <summary>
    /// Invokes the Explorer window's own "new tab" button.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> once the button has been invoked (the tab itself appears asynchronously);
    /// <see langword="false"/> when there is no such button to invoke, or UI Automation failed.
    /// </returns>
    public static bool TryOpenNewTab(IntPtr explorerHwnd)
    {
        if (explorerHwnd == IntPtr.Zero) return false;

        try
        {
            var button = FindNewTabButton(AutomationElement.FromHandle(explorerHwnd));
            if (button is null) return false;

            // InvokePattern is a cross-process call into Explorer: it lets the shell run its own real
            // "new tab" command rather than trying to recreate what that command does.
            if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern)) return false;
            ((InvokePattern)pattern).Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ExplorerTabUiAutomation] Could not ask Explorer for a new tab: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    private static AutomationElement? FindNewTabButton(AutomationElement? window)
    {
        if (window is null) return null;

        // Scoped to the tab strip rather than the whole window, so the search can never land on an
        // unrelated "+"/"New" button elsewhere in Explorer's command bars. The class-name lookup is the
        // exact one observed on a live Windows 11 window; the ControlType.Tab fallback keeps this working
        // if the tab strip is a different implementation (which is also the shape the whole feature takes
        // on a build where no TabView exists at all -- no strip, no button, no tab, normal open instead).
        var tabStrip = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, TabViewClassName))
                       ?? window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tab));

        return (tabStrip ?? window).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, NewTabButtonId));
    }
}
