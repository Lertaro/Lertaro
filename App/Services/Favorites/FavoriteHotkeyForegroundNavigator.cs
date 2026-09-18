using System.IO;
using System.Text;
using Lertaro.Core;
using Lertaro.Core.Hook;
using Lertaro.Core.Hook.Commands;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.App.Services.Favorites;

/// <summary>The foreground window that turned out to be a file manager this app can navigate.</summary>
public sealed record FavoriteHotkeyTarget(IntPtr Window, string ClassName, string ProcessName);

/// <summary>
/// Resolves the window that is currently in the foreground to a file manager this app can navigate, and
/// asks that manager to go to a favorite's folder.
/// </summary>
/// <remarks>
/// Reuse, not a second navigation implementation. Which managers exist is the adapter registry's answer
/// (an unrecognized foreground window is left alone), and the navigation itself goes through the same
/// App -> Hook route every other adapter caller uses -- see <see cref="NavigateExistingWindow"/> for why
/// that route and not a direct call.
/// </remarks>
internal sealed class FavoriteHotkeyForegroundNavigator
{
    public void Navigate(string rawPath)
    {
        // Every failure below is logged and swallowed: this runs from a WM_HOTKEY handler, and an
        // exception there would come out of the message pump.
        try
        {
            var path = UserPathResolver.Resolve(rawPath);
            var target = ResolveForegroundManager(FavoriteHotkeyNavigation.GetForegroundWindow());

            var decision = FavoriteHotkeyNavigation.Decide(
                isWebUrl: Helpers.FavoriteUrlHelper.IsWebUrl(rawPath),
                isVirtualPath: UserPathResolver.IsVirtualPath(path),
                targetIsFolder: IsFolderPath(path),
                managerCanNavigateInPlace: target != null);

            Execute(decision, path, target);
        }
        catch (Exception ex)
        {
            Logger.Log($"[FavoriteHotkeys] Navigation failed for '{rawPath}': {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    /// What to do once the decision above has been made. Separate from <see cref="Navigate"/> so the
    /// intent is readable on its own; <paramref name="target"/> is guaranteed non-null for the
    /// <see cref="FavoriteHotkeyNavigationAction.NavigateInPlace"/> case by the decision itself.
    /// </summary>
    private static void Execute(FavoriteHotkeyNavigationAction decision, string path, FavoriteHotkeyTarget? target)
    {
        switch (decision)
        {
            case FavoriteHotkeyNavigationAction.NavigateInPlace when target != null:
                NavigateExistingWindow(path, target);
                return;

            case FavoriteHotkeyNavigationAction.OpenThroughDefaultRoute:
                FileExecutor.LocateInExplorer(path);
                return;

            case FavoriteHotkeyNavigationAction.UnusableTarget:
                Logger.Log($"[FavoriteHotkeys] '{path}' is not a folder a file manager can navigate to.", LogLevel.Debug);
                return;

            default:
                // NotAFileManager. Deliberately nothing: the window in front is not a file manager this
                // app supports, and opening a new one over the user's work is the wrong answer.
                Logger.Log("[FavoriteHotkeys] Foreground window is not a supported file manager; ignoring.", LogLevel.Debug);
                return;
        }
    }

    /// <summary>
    /// Makes the foreground window's own file manager go to <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// Routed through <see cref="InlineAdapterIpcCoordinator"/> to the Hook process rather than calling
    /// the adapter's own <c>ExecuteItem</c> here, exactly like Quick Navigation and inline search's
    /// Enter-to-execute do. Two reasons, and the first is fatal to the direct call:
    /// <list type="bullet">
    /// <item>The adapters' navigation is STA-affine COM interop (Explorer's IShellWindows/Navigate2/
    /// SelectItem, OneCommander's UI Automation). This method runs off a thread-pool thread because the
    /// coordinator's wait would otherwise block the message pump, and a thread-pool thread is MTA -- so a
    /// direct call would put the required Explorer case through a cross-apartment marshal it does not
    /// support. The Hook runs each adapter call on its own freshly-spun STA thread.</item>
    /// <item>Adapters for a manager that is running elevated need the Hook's elevation; the App never
    /// elevates itself, so a direct call would have its window messages dropped by UIPI.</item>
    /// </list>
    /// <c>isDir: true</c> is the caller's already-known answer, so the coordinator marks the path as a
    /// directory itself (the adapters' trailing-separator convention) and no adapter re-derives it.
    /// </remarks>
    private static void NavigateExistingWindow(string path, FavoriteHotkeyTarget target)
    {
        var window = target.Window;
        var folderPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (folderPath.Length == 0) return;

        // Off the UI thread: the coordinator blocks for up to a second waiting for the Hook to confirm,
        // and this is called from the message pump's WM_HOTKEY handler. The coordinator and the Hook both
        // resolve everything they need on their own side, so nothing here crosses a thread boundary.
        _ = Task.Run(() =>
        {
            try
            {
                // The window can be gone by the time this runs; navigating a recycled handle would act
                // on whatever window inherited it.
                if (!ExplorerNativeHooks.IsWindow(window)) return;

                var hookClient = App.HookClient;
                if (hookClient?.IsConnected != true)
                {
                    Logger.Log("[FavoriteHotkeys] The Hook process is not connected; not navigating.", LogLevel.Debug);
                    return;
                }

                if (InlineAdapterIpcCoordinator.ExecuteItem(window, folderPath, isDir: true, string.Empty, hookClient.SendMessage, out _))
                    return;

                // A timeout is not a confirmed failure -- see the coordinator's own remarks. There is
                // deliberately no fallback here: every fallback would OPEN a folder in a new or existing
                // window, which is the one thing this feature must not do when the user asked to navigate
                // the window already in front of them.
                Logger.Log($"[FavoriteHotkeys] '{target.ProcessName}' did not confirm navigating to '{folderPath}'.", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                Logger.Log($"[FavoriteHotkeys] Navigating the foreground file manager failed: {ex.Message}", LogLevel.Error);
            }
        });
    }

    /// <summary>
    /// The foreground window and its root owner, first of which an inline-search adapter recognizes.
    /// Both are tried because a manager is not always focused on its own top-level window: a Directory
    /// Opus lister hosts its file displays as child windows, so the foreground window can be one of
    /// those, and the adapters that handle a child report paths for that child rather than its parent.
    /// </summary>
    internal static FavoriteHotkeyTarget? ResolveForegroundManager(IntPtr foregroundWindow)
    {
        if (foregroundWindow == IntPtr.Zero) return null;

        var root = ExplorerNativeHooks.GetAncestor(foregroundWindow, ExplorerNativeHooks.GA_ROOTOWNER);
        if (root == IntPtr.Zero) root = foregroundWindow;

        return Match(foregroundWindow) ?? (root != foregroundWindow ? Match(root) : null);
    }

    private static FavoriteHotkeyTarget? Match(IntPtr window)
    {
        var className = GetClassName(window);
        var processName = GetProcessName(window);

        // Recognition only -- the adapter instance is not carried out of here, because the Hook resolves
        // its own when it executes. CanRecognizeHost, not CanHandle: recognizing the manager must not
        // depend on the user having inline search switched on for it, the same distinction Quick
        // Navigation draws.
        return RecognizesHost(window, className, processName)
            ? new FavoriteHotkeyTarget(window, className, processName)
            : null;
    }

    private static bool RecognizesHost(IntPtr window, string className, string processName)
    {
        foreach (var candidate in PluginSdk.Registries.InlineSearchAdapterRegistry.GetAllAdapters())
        {
            try
            {
                if (candidate.CanRecognizeHost(window, className, processName)) return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"[FavoriteHotkeys] Adapter '{candidate.Name}' host check threw: {ex.Message}", LogLevel.Error);
            }
        }

        return false;
    }

    internal static string GetClassName(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        ExplorerNativeHooks.GetClassName(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    /// <summary>
    /// The window's owning process name, without its extension -- which is the form every adapter's
    /// host check compares against. Read the same way <c>QuickPanelManager.ProcessNameOf</c> does: a
    /// window can be gone before its process is asked for, and that is simply "no manager matched".
    /// </summary>
    private static string GetProcessName(IntPtr window)
    {
        try
        {
            ExplorerNativeHooks.GetWindowThreadProcessId(window, out var processId);
            if (processId == 0) return string.Empty;

            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Whether the favorite resolves to an existing folder. Uses the trailing-separator convention
    /// rather than comparing against the trimmed path: a configured path may legitimately keep its own
    /// trailing separator, so "did trimming change it" would answer yes for every favorite typed that
    /// way and report a folder as a file.
    /// </summary>
    private static bool IsFolderPath(string path)
    {
        try
        {
            var trimmed = Path.TrimEndingDirectorySeparator(path);
            return Directory.Exists(trimmed) || path.Length > trimmed.Length;
        }
        catch
        {
            return false;
        }
    }
}
