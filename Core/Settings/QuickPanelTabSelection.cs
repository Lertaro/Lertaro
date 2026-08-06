namespace Lertaro.Core;

/// <summary>
/// Which workspace the panel should come up on for the app it is being summoned over. Pure, so the
/// rule can be stated once and tested: the panel itself only supplies the process name it read from
/// the foreground window.
/// </summary>
public static class QuickPanelTabSelection
{
    /// <summary>
    /// The id of the first tab that claims <paramref name="processName"/>, or null when none does --
    /// in which case the caller keeps whatever tab was last active. First match rather than best:
    /// claiming the same app from two workspaces is a contradiction only the user can resolve, and
    /// silently picking one by some scoring rule would hide that they had.
    /// </summary>
    public static string? SelectTabId(string? processName, IEnumerable<QuickPanelTab>? tabs)
    {
        if (string.IsNullOrEmpty(processName) || tabs == null)
            return null;

        foreach (var tab in tabs)
        {
            if (ProcessNameFilter.Matches(processName, tab.Processes))
                return tab.Id;
        }
        return null;
    }

    /// <summary>
    /// Whether the panel should refuse to open over this app at all: the global blacklist plus the
    /// panel's own, never one instead of the other. Anything the user blocked globally is blocked here
    /// too, and the panel's list only ever adds to it.
    /// </summary>
    /// <remarks>
    /// The hotkey path is already gated upstream by the keyboard hook, which consults the global list --
    /// but that gate exempts file dialogs (see KeyboardHookService), and Toggle() is reachable without
    /// going through the hook at all. Checking both lists here makes the rule true whichever way the
    /// panel was asked to open, rather than true by coincidence of the route in.
    /// </remarks>
    public static bool IsBlocked(string? processName, UserSettings? settings)
        => settings != null
           && (ProcessNameFilter.Matches(processName, settings.BlacklistedProcesses)
               || ProcessNameFilter.Matches(processName, settings.QuickPanel?.BlacklistedProcesses));
}
