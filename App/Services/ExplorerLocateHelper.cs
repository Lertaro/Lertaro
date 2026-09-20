using System.Diagnostics;
using System.IO;
using Lertaro.Core;
using Lertaro.PluginSdk.Helpers;
using MessageBox = Lertaro.App.Views.Controls.Dialogs.CustomMessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace Lertaro.App.Services;

// "Select this item in Explorer" -- split out of FileExecutor to keep that file under the line-count
// limit. Routes through the shell (SHOpenFolderAndSelectItems for the fallback, an already-open window's
// own Navigate2 for the in-place case) so it respects the user's default file manager instead of always
// opening explorer.exe. The live-window half of that -- ShellWindows COM, tab handles -- lives in
// ExplorerShellWindowsHelper.
internal static class ExplorerLocateHelper
{
    /// <summary>
    /// Opens the folder holding <paramref name="path"/> with that item selected. Returns immediately;
    /// the shell work runs on a ShellThread, which is where the reasoning for that lives.
    /// </summary>
    public static void LocateInExplorer(string path) =>
        ShellThread.Run("ExplorerLocate", () => LocateInExplorerCore(string.IsNullOrWhiteSpace(path) ? path : UserPathResolver.Expand(path)));

    private static void LocateInExplorerCore(string path)
    {
        // Expand environment variables first so "locate in Explorer" sees the same resolved path
        // that FileExecutor.LaunchExistingPath already uses for opening favorites. A virtual path is
        // deliberately left virtual here rather than resolved like FileExecutor's own callers do: the
        // shell parses the token itself further down, and Path.GetDirectoryName("shell:...") is empty,
        // which is exactly what routes a virtual item to the shell-locate fallback.
        path = UserPathResolver.Expand(path);

        // A user-configured default file manager (see GitHub issue #180, FileExecutor.
        // TryBuildDefaultFileManagerStartInfo) takes over "open containing folder" too -- it can only open
        // the folder itself, not select-and-highlight the specific item within it the way the shell select
        // API below does, since there's no generic way to know a third-party tool's own "select this item"
        // argument syntax. Accepted tradeoff: one generic open-folder method reused everywhere, rather
        // than each caller needing its own opinion about the setting.
        var folder = ResolveContainingFolder(path, Directory.Exists);
        var fileManager = UserSettings.Load().DefaultFileManager;
        if (!string.IsNullOrEmpty(folder) && FileExecutor.TryBuildDefaultFileManagerStartInfo(folder, fileManager) is { } customStartInfo)
        {
            try
            {
                Process.Start(customStartInfo);
                return;
            }
            catch (Exception ex)
            {
                Logger.Log($"[FileExecutor] Default file manager launch failed for '{folder}': {ex.Message}", LogLevel.Error);
                MessageBox.Show(string.Format(TranslationManager.Instance["Executor_LocateFailed"], ex.Message), TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        if (fileManager.OpenFoldersInNewExplorerTabs && FileExecutor.TryLocateInNewExplorerTab(path, () => ShellOpenHelper.TryRevealInFolder(path)))
            return;

        RevealWithShell(path, folder);
    }

    /// <summary>
    /// The folder a locate should end up looking at: the item's own folder, or the item itself when it is
    /// one.
    /// </summary>
    /// <remarks>
    /// Pure apart from the existence probe handed in, so the branch is covered by a test. A null means
    /// "nothing to show" -- a drive root, or a virtual token that is not a folder on disk.
    /// </remarks>
    internal static string? ResolveContainingFolder(string path, Func<string, bool> directoryExists)
        => directoryExists(path) ? path : Path.GetDirectoryName(path);

    /// <summary>
    /// Selects <paramref name="path"/> inside an Explorer window that is already open.
    /// </summary>
    /// <returns><see langword="false"/> when no open window could be driven, so the caller falls back.</returns>
    public static bool TryLocateInExistingExplorer(string path, IntPtr explorerHwnd)
    {
        if (explorerHwnd == IntPtr.Zero) return false;
        // A configured default file manager should win over this Explorer shortcut -- refusing it here
        // forces the caller to fall through to LocateInExplorer, where the actual custom-manager launch
        // lives. When new-tab integration is enabled, use the tracked Explorer window as its target
        // rather than navigating the current tab below.
        var fileManager = UserSettings.Load().DefaultFileManager;
        if (fileManager is { Enabled: true }) return false;
        if (fileManager.OpenFoldersInNewExplorerTabs)
            return FileExecutor.TryLocateInNewExplorerTab(path, preferredExplorerWindow: explorerHwnd);

        // The parent, whatever the item is. Navigating to the item itself when it happened to be a
        // folder made "open containing folder" step INTO that folder and select nothing -- which is
        // just what "open" does, and not what the action says. A drive root has no parent to show,
        // so it falls through to LocateInExplorer rather than pretending it worked.
        var targetFolder = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
        {
            return false;
        }

        object? window = null;
        try
        {
            // A zero tab handle -- an Explorer window without tabs -- means "this window's only view".
            window = ExplorerShellWindowsHelper.FindShellWindowForTab(ExplorerShellWindowsHelper.GetActiveTabHandle(explorerHwnd), explorerHwnd);
            if (window == null) return false;

            // Folders get selected too, for the same reason: the gate used to be File.Exists, so a
            // located folder was never highlighted once the window arrived.
            ExplorerShellWindowsHelper.NavigateAndSelect(window, targetFolder, Path.GetFileName(path));
            return true;
        }

        catch (Exception ex)
        {
            Logger.Log($"[FileExecutor] Locate in existing explorer failed for '{path}': {ex.Message}", LogLevel.Error);
            return false;
        }
        finally
        {
            ExplorerShellWindowsHelper.ReleaseComObject(window);
        }
    }

    // Last resort for a locate: the documented shell routes, in decreasing fidelity. Selecting the item
    // is what the action promises; when the shell will not do that -- a virtual item with nowhere to be
    // selected in, a path it cannot parse -- opening the folder without the highlight is still closer to
    // the request than an error dialog. Both go through ShellOpenHelper, so a locate ends up where every
    // other "show me this folder" in the app does.
    private static void RevealWithShell(string path, string? folder)
    {
        if (ShellOpenHelper.TryRevealInFolder(path)) return;
        if (!string.IsNullOrEmpty(folder) && ShellOpenHelper.TryOpenFolder(folder)) return;

        Logger.Log($"[FileExecutor] Locate failed for '{path}': the shell could neither select the item nor open its folder.", LogLevel.Error);
        MessageBox.Show(string.Format(TranslationManager.Instance["Executor_LocateFailed"], path), TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
