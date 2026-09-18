using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.WindowSwitcher;

/// <summary>
/// Dynamic action menu for the window an instant result points at: toggle always-on-top, hide/show,
/// maximize, minimize, restore, close, and focus. The target is identified by the instant result's own
/// <c>activatewindow:&lt;hwnd&gt;</c> action argument, so this offers itself only when exactly one such
/// result is selected (see <see cref="WindowMenuOperations.ParseWindowHandle"/>).
/// </summary>
public class WindowMenuActionProvider : IDynamicActionProvider
{
    // Higher than ShellMenuActionProvider's -1 so the window-specific actions sit below the generic
    // shell menu in the actions list -- they act on the window, not on a file path.
    public int Priority => 1;

    public string GroupName => TranslationService.Get("WindowSwitcher_MenuGroup");

    public bool CanProvide(IReadOnlyList<ISearchResult> results) => TryGetTarget(results, out _);

    public IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, IntPtr hMenu)
    {
        // No submenus: anything but the root menu is not ours.
        if (hMenu != IntPtr.Zero) yield break;
        if (!TryGetTarget(results, out var hwnd)) yield break;

        // Read the state live, so the labels and the enabled flags describe the window as it is right
        // now; the decision itself is the pure, tested Build.
        var state = WindowMenuOperations.ReadState(hwnd);

        foreach (var entry in WindowMenuOperations.Build(state))
        {
            // Captured per iteration: every entry's delegate must close over its OWN command id.
            var command = entry.CommandId;
            yield return new DynamicMenuItem
            {
                Text = TranslationService.Get(entry.LabelKey),
                CommandId = command,
                IsDisabled = !entry.Enabled,
                OnExecute = () => Execute(hwnd, command)
            };
        }
    }

    /// <summary>
    /// The command-id half of the same dispatch. The host normally executes a dynamic item through its
    /// <see cref="DynamicMenuItem.OnExecute"/> delegate, but it may route by id instead, so both paths
    /// land on <see cref="Execute"/>.
    /// </summary>
    public void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, IntPtr ownerHwnd)
    {
        if (!TryGetTarget(results, out var hwnd)) return;
        Execute(hwnd, commandId);
    }

    // No per-session state to set up or release: every call re-reads the target window from the
    // selection it was handed, and the Win32 calls hold nothing open between calls.
    public void Init() { }

    public void ClearSession() { }

    private static void Execute(IntPtr hwnd, uint commandId)
    {
        switch ((WindowMenuOperations.MenuCommand)commandId)
        {
            case WindowMenuOperations.MenuCommand.ToggleTopmost:
                // Flip relative to the state at click time, not to a value captured when the menu was
                // built -- the user may have toggled it from the titlebar in between.
                WindowMenuOperations.SetTopmost(hwnd, !WindowMenuOperations.ReadState(hwnd).IsTopmost);
                break;
            case WindowMenuOperations.MenuCommand.HideOrShow:
                WindowMenuOperations.SetVisible(hwnd, !WindowMenuOperations.ReadState(hwnd).IsVisible);
                break;
            case WindowMenuOperations.MenuCommand.Maximize:
                WindowMenuOperations.Maximize(hwnd);
                break;
            case WindowMenuOperations.MenuCommand.Minimize:
                WindowMenuOperations.Minimize(hwnd);
                break;
            case WindowMenuOperations.MenuCommand.Restore:
                WindowMenuOperations.Restore(hwnd);
                break;
            case WindowMenuOperations.MenuCommand.Close:
                WindowMenuOperations.Close(hwnd);
                break;
            case WindowMenuOperations.MenuCommand.Focus:
                WindowMenuOperations.Focus(hwnd);
                break;
        }
    }

    /// <summary>
    /// Resolves the single selected result's window handle. False unless exactly one result is selected
    /// and it carries a real <c>activatewindow:</c> argument -- a multiple selection has no single
    /// target window, and every other result kind (files, plugin actions) has no argument at all.
    /// </summary>
    private static bool TryGetTarget(IReadOnlyList<ISearchResult> results, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        if (results.Count != 1) return false;

        hwnd = WindowMenuOperations.ParseWindowHandle(results[0].InstantActionArgument);
        return hwnd != IntPtr.Zero;
    }
}
