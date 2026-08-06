using System.IO;
using System.Diagnostics;

using Lertaro.Core.Wire;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.Core.Hook.Commands;
namespace Lertaro.App.Services.ShellMenu.QuickNav;

// Everything about "what was the active host" that NavigateOrOpen needs, captured once by the caller
// BEFORE showing any UI of its own (e.g. QuickNavigationMenu.Show, right when it reads ExplorerTracker) --
// re-reading ExplorerTracker live at click time is not safe: a Quick Navigation popup can sit open for a
// while, and its own helper window stealing (then later releasing) foreground perturbs ExplorerTracker's
// live state in the meantime. DialogHwnd being IntPtr.Zero means no dialog was active at capture time;
// ActiveAdapter being null means no file-manager adapter matched the active host at capture time.
public readonly record struct QuickNavTriggerContext(IntPtr DialogHwnd, IntPtr ActiveHwnd, IInlineSearchAdapter? ActiveAdapter, bool IsDesktop);

public static class QuickNavigationNavigator
{
    // isDir: already known by the caller (e.g. QuickNavigationMenu's item.HasSubMenu) -- see
    // InlineAdapterIpcCoordinator.ExecuteItem for why the Hook process must never be asked to re-derive
    // this itself via Directory.Exists/File.Exists.
    public static void NavigateOrOpen(string path, bool isDir, QuickNavTriggerContext trigger = default)
    {
        // Web-address favorites: straight to the default browser. No host file-manager adapter understands
        // a URL as a filesystem path, so this must short-circuit before any adapter delegation below.
        if (Helpers.FavoriteUrlHelper.IsWebUrl(path))
        {
            try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); } catch { }
            return;
        }

        if (trigger.DialogHwnd != IntPtr.Zero)
        {
            // A dialog's filename box submits (fires Open/Save) if given a complete, existing file path --
            // never let picking a file from a Quick Navigation menu auto-confirm the dialog on the user's
            // behalf; only ever navigate it to a directory, same as clicking a folder would.
            var dialogTarget = isDir ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dialogTarget)) return;

            App.HookClient?.SendMessage(new IpcMessage
            {
                Id = IpcMessageId.NavigateDialog,
                Hwnd = trigger.DialogHwnd.ToInt64(),
                StringVal1 = dialogTarget
            });
            return;
        }

        // Delegate to whichever file-manager adapter matched the active host (Explorer, Directory Opus,
        // Total Commander, ...) so a folder navigates that window and a file opens/selects there -- the
        // same adapter inline search already uses to execute a result. Uses the captured trigger.ActiveHwnd/
        // ActiveAdapter/IsDesktop, not a live ExplorerTracker re-read, for the same staleness reason as
        // trigger.DialogHwnd above -- the Hook still re-resolves the adapter for trigger.ActiveHwnd itself
        // (see InlineAdapterCommandHandler.ResolveAdapter) if its own tracker has since moved on, so this
        // stays correct even though the hwnd was captured a while ago.
        if (trigger.ActiveAdapter != null && trigger.ActiveHwnd != IntPtr.Zero && App.HookClient?.IsConnected == true)
        {
            if (InlineAdapterIpcCoordinator.ExecuteItem(trigger.ActiveHwnd, path, isDir, string.Empty, App.HookClient.SendMessage, out var lateResult))
                return;

            // Timed out without a confirmed result -- some adapters make blocking calls with no timeout of
            // their own (e.g. Total Commander's SendMessage), so the Hook-side call can still legitimately
            // be in flight rather than genuinely dead. Falling back to Process.Start right here used to be
            // able to race that: the file gets launched/opened by the fallback AND separately
            // navigated-to-and-selected by the adapter call finishing a moment later. See
            // InlineAdapterIpcCoordinator.RunAfterLateResultAsync -- also used by inline search's own
            // Enter-to-execute for the identical race -- for why waiting on the same in-flight call a bit
            // longer, off the UI thread, closes that window without blocking the caller.
            _ = InlineAdapterIpcCoordinator.RunAfterLateResultAsync(lateResult, onSuccess: () => { }, onFallback: () => OpenDirectly(path, isDir, trigger.IsDesktop));
            return;
        }

        OpenDirectly(path, isDir, trigger.IsDesktop);
    }

    // This is a NAVIGATION menu -- picking a file should land on it (selected, in its folder) rather than
    // launch it, UNLESS the active host is the desktop: there's no Explorer pane to navigate-and-select
    // within there, so acting on an item means opening it directly, same as double-clicking either would on
    // the desktop -- mirrors ExplorerInlineSearchAdapter.ExecuteItem's own identical desktop branch, which
    // this fallback only runs alongside when no adapter/Hook connection is available to make that call in
    // the first place.
    private static void OpenDirectly(string path, bool isDir, bool isDesktop)
    {
        if (!isDir && !isDesktop)
        {
            FileExecutor.LocateInExplorer(path);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
