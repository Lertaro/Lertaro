using System.IO;
using System.Runtime.InteropServices;
using Lertaro.App.Services;
using Lertaro.App.Services.Plugin;
using Lertaro.Core;
using Lertaro.Core.Wire;
using Lertaro.Core.Hook.Commands;
namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

public static class InlineSearchNavigator
{
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    public static void LocateInExplorerExternal(this Lertaro.App.InlineSearchWindow window, string path)
    {
        var tracker = window.Manager.ExplorerTracker;
        if (tracker.IsExplorerOrDesktopActive && !tracker.IsDesktop && tracker.ActiveHwnd != IntPtr.Zero)
        {
            if (FileExecutor.TryLocateInExistingExplorer(path, tracker.ActiveHwnd))
            {
                return;
            }
        }

        FileExecutor.LocateInExplorer(path);
    }

    // forceRealOpen: true -- this is the explicit "Open"/"Open (Admin)" action, reached by the user
    // deliberately choosing it from the actions menu rather than Enter's own "do the fastest sensible
    // thing" shortcut on a plain result (see ExecuteSearchResult, forceRealOpen: false there). Explicitly
    // choosing "Open" should always mean a real open -- launch the file, or a fresh Explorer window at the
    // folder -- never silently redirect into navigating whatever's currently docked/already open instead.
    public static void OpenFileOrFolderExternal(this Lertaro.App.InlineSearchWindow window, string path) => window.OpenPathFromInline(path, asAdmin: false, ResolveIsDir(path), forceRealOpen: true);

    public static void OpenFileOrFolderAsAdminExternal(this Lertaro.App.InlineSearchWindow window, string path) => window.OpenPathFromInline(path, asAdmin: true, ResolveIsDir(path), forceRealOpen: true);

    // null means "doesn't exist" -- distinct from false ("is a file"), so OpenPathFromInline can skip
    // ExecuteItem entirely for a path that no longer exists rather than asking an adapter to navigate a
    // third-party app to it. Directory.Exists(path) alone can't tell "is a file" apart from "doesn't exist
    // at all" (both false), which is exactly the ambiguity that needs resolving here.
    private static bool? ResolveIsDir(string path)
    {
        if (Directory.Exists(path)) return true;
        if (File.Exists(path)) return false;
        return null;
    }

    public static void ExecuteSearchResult(this Lertaro.App.InlineSearchWindow window, AppSearchResult result, bool asAdmin = false)
    {
        if (result.IsSearchSectionHeader)
            return;
        if (result.IsPluginSearchAction)
        {
            if (PluginManager.Instance.TryExecuteSearchAction(result, window, asAdmin))
            {
                window.HideWindow();
            }

            return;
        }

        if (PluginManager.Instance.TryExecuteSearchAction(result, window, asAdmin))
        {
            window.HideWindow();
            return;
        }

        // Trust result.IsDir for *which kind* it is (that's already known from the index), but still
        // confirm the path actually still exists right now -- a search result can go stale between when it
        // was indexed and when the user acts on it (e.g. the file was deleted in between).
        var exists = result.IsDir ? Directory.Exists(result.FullPath) : File.Exists(result.FullPath);
        window.OpenPathFromInline(result.FullPath, asAdmin, exists ? result.IsDir : (bool?)null);
    }

    // isDir: null means the path doesn't exist (skip ExecuteItem, go straight to the "not found" fallback
    // below); otherwise the caller's already-known answer for file vs directory -- see
    // InlineAdapterIpcCoordinator.ExecuteItem for why the Hook process must never be asked to re-derive
    // this itself via Directory.Exists/File.Exists.
    // forceRealOpen: skips every "control some OTHER already-open window instead" shortcut below (adapter
    // navigate, dialog navigate, reuse-an-existing-Explorer locate) so the explicit "Open" action always
    // does what it says -- launch the file, or a fresh Explorer window at the folder -- rather than
    // silently redirecting into navigating whatever's currently docked/open. Enter's own quick-execute
    // shortcut (ExecuteSearchResult) leaves this false, keeping its current "fastest sensible thing"
    // behavior unchanged.
    private static void OpenPathFromInline(this Lertaro.App.InlineSearchWindow window, string path, bool asAdmin, bool? isDir, bool forceRealOpen = false)
    {
        var tracker = window.Manager.ExplorerTracker;
        if (!forceRealOpen && !asAdmin && isDir.HasValue && path != "__SHOW_MORE__" && tracker.ActiveInlineAdapter != null && tracker.ActiveHwnd != IntPtr.Zero && App.HookClient?.IsConnected == true
            && !OpeningAFolderBelongsToTheFileManager(tracker.IsDesktop, isDir == true, UserSettings.Load().DefaultFileManager))
        {
            // Captured once here, before HideWindow(), and reused below instead of reading
            // tracker.ActiveHwnd again afterward: hiding the window can itself cause the tracker to
            // re-evaluate what's active on another thread, and re-reading raced that -- confirmed by
            // diagnostic logging sending Hwnd=0 to the Hook, which silently drops a zero-hwnd message
            // with no response, so the caller always burned the full ~5s wait before falling back.
            var targetHwnd = tracker.ActiveHwnd;

            // Hide before the actual navigate call, not after: ExecuteItem below blocks the UI thread for
            // up to 1s waiting for the Hook process to confirm the third-party adapter's own native call
            // finished (some, like Total Commander's SendMessage with its own internal 150ms sleep, are
            // slow enough that this made the window visibly linger after the user already committed to the
            // click). Closing the window is a UI-only concern -- whether the navigate call itself
            // eventually succeeds or falls back below doesn't need it to still be open to decide.
            window.Manager.IsExecuting = true;
            window.HideWindow();

            if (InlineAdapterIpcCoordinator.ExecuteItem(targetHwnd, path, isDir.Value, window.SearchText, App.HookClient.SendMessage, out var lateResult))
            {
                return;
            }

            // Timed out without a confirmed result -- some adapters make blocking calls with no timeout of
            // their own (e.g. Total Commander's SendMessage), so the Hook-side call can still legitimately
            // be in flight rather than genuinely dead. Falling straight into the fallback chain below used
            // to be able to race that: for a file, the fallback launches/opens it directly, and the adapter
            // call can then finish a moment later and ALSO navigate-and-select it in the host app. See
            // InlineAdapterIpcCoordinator.RunAfterLateResultAsync -- also used by the Quick Navigation menu
            // for the identical race -- for why waiting on the same in-flight call a bit longer, off the UI
            // thread, resolves that without blocking the caller. Its continuations resume back on this UI
            // thread automatically via WPF's SynchronizationContext, so touching window/UI state here is
            // safe -- the window is already hidden by this point either way.
            _ = InlineAdapterIpcCoordinator.RunAfterLateResultAsync(lateResult,
                onSuccess: () => window.Manager.IsExecuting = false,
                onFallback: () => { window.Manager.IsExecuting = false; window.RunFallbackChain(path, asAdmin, isDir, forceRealOpen); });
            return;
        }

        window.RunFallbackChain(path, asAdmin, isDir, forceRealOpen);
    }

    /// <summary>
    /// Whether asking the host to go to a folder would really be OPENING it, and so belongs to the
    /// configured default file manager instead.
    /// </summary>
    /// <remarks>
    /// The desktop matches the Explorer adapter like any Explorer window does (Progman/WorkerW, see
    /// ExplorerInlineSearchAdapter.CanHandle), but unlike one it has nothing to navigate in place --
    /// Explorer answers by opening a brand new window, which is precisely the act
    /// "open folders with a third-party file manager" exists to redirect. Inside a real Explorer window
    /// the shortcut stays: navigating the window the user is already standing in is the point of inline
    /// search, not an open.
    ///
    /// The same rule already guards the other shortcut of this kind
    /// (ExplorerLocateHelper.TryLocateInExistingExplorer, which refuses outright when the setting is on).
    /// This one never had it, so a folder opened from the desktop went to Explorer -- and only sometimes,
    /// since an adapter call that timed out fell through to the fallback chain, which does honour it.
    /// </remarks>
    internal static bool OpeningAFolderBelongsToTheFileManager(bool isDesktop, bool isDir, DefaultFileManagerSetting? fileManager)
        => isDesktop && isDir && fileManager is { Enabled: true } && !string.IsNullOrWhiteSpace(fileManager.Path);

    private static void RunFallbackChain(this Lertaro.App.InlineSearchWindow window, string path, bool asAdmin, bool? isDir, bool forceRealOpen = false)
    {
        var tracker = window.Manager.ExplorerTracker;

        if (!forceRealOpen && path != "__SHOW_MORE__" && tracker.IsExplorerOrDesktopActive && tracker.IsActiveWindowDialog && tracker.ActiveHwnd != IntPtr.Zero)
        {
            // Only a folder-only target (WinRAR/Bandizip's own "extract to" field, see
            // IFileDialogAdapter.TargetIsFolderOnly) needs a picked FILE resolved to its containing folder
            // first -- an Open/Save dialog's filename box (ClassicFileDialogAdapter/StandardFileDialogAdapter)
            // wants the exact path unchanged, same as it always has. Resolved here, in the App process
            // rather than the adapter's own NavigateTo, since that runs in the elevated Hook process (see
            // InlineAdapterCommandHandler's own comment on why) -- this process can actually tell file from
            // folder reliably, including for a network drive the interactive user mapped without elevation.
            // isDir == null (a stale result whose file no longer exists) is treated the same as a file, not
            // a directory, for the identical reason.
            var dialogTarget = tracker.ActiveAdapter?.TargetIsFolderOnly == true && isDir != true
                ? Path.GetDirectoryName(path)
                : path;
            if (!string.IsNullOrEmpty(dialogTarget))
            {
                App.HookClient?.SendMessage(new IpcMessage
                {
                    Id = IpcMessageId.NavigateDialog,
                    Hwnd = tracker.ActiveHwnd.ToInt64(),
                    StringVal1 = dialogTarget

                });

                window.UpdateSearchDisplay(string.Empty);
                window.HideWindow();
                return;
            }
        }

        if (!forceRealOpen
            && isDir == true
            && tracker.IsExplorerOrDesktopActive

            && !tracker.IsDesktop

            && tracker.ActiveHwnd != IntPtr.Zero

            && FileExecutor.TryLocateInExistingExplorer(path, tracker.ActiveHwnd))
        {
            window.HideWindow();
            return;
        }

        var searchText = window.SearchText;
        if (path != "__SHOW_MORE__")
        {
            window.HideWindow();
            if (asAdmin)
                FileExecutor.OpenFileOrFolderAsAdmin(path, searchText);
            else
                FileExecutor.OpenFileOrFolder(path, searchText);
            return;
        }

        FileExecutor.OpenFileOrFolder(path, searchText, () => window.HideWindow());
    }

    public static void ResetInlineSearchAndFocusDialog(this Lertaro.App.InlineSearchWindow window)
    {
        // 1. Clear our own search query

        window.UpdateSearchDisplay(string.Empty);

        // 2. Grant the elevated hook service permission to call SetForegroundWindow,
        //    bypassing the system's foreground-lock without needing to hide this window.

        if (App.HookClient != null && App.HookClient.ServiceProcessId != 0)
        {
            AllowSetForegroundWindow(App.HookClient.ServiceProcessId);
        }

        // 3. Ask the elevated service to restore focus to the dialog's edit box

        var tracker = window.Manager.ExplorerTracker;
        if (tracker.ActiveHwnd != IntPtr.Zero && App.HookClient != null)
        {
            App.HookClient.SendMessage(new IpcMessage
            {
                Id = IpcMessageId.RestoreDialogFocus,
                Hwnd = tracker.ActiveHwnd.ToInt64()

            });
        }
    }
}
