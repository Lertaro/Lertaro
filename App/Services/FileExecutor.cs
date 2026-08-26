using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Lertaro.Core;
using Lertaro.App.Services.AppWindow;
using MessageBox = Lertaro.App.Views.Controls.Dialogs.CustomMessageBox;
namespace Lertaro.App.Services;

public static class FileExecutor
{
    // ponytail: Explorer has no stable API to associate simultaneous tab requests with their callers.
    // Serialize folder opens while the experimental tab option is enabled; a public Explorer-tab API is the upgrade path.
    private static readonly SemaphoreSlim FolderOpenGate = new(1, 1);

    public static void OpenFileOrFolder(string path, string currentSearchText = "", Action? onHideWindow = null) => OpenFileOrFolderCore(path, currentSearchText, onHideWindow, asAdmin: false);

    public static void OpenFileOrFolderAsAdmin(string path, string currentSearchText = "", Action? onHideWindow = null) => OpenFileOrFolderCore(path, currentSearchText, onHideWindow, asAdmin: true);

    private static void OpenFileOrFolderCore(string path, string currentSearchText, Action? onHideWindow, bool asAdmin)
    {
        if (path == "__NO_RESULTS__")
            return;
        if (path == "__SHOW_MORE__")
        {
            // The full window takes over from whatever asked for it, so a preview the user had open
            // carries across instead of closing with the window it was opened from. Read before
            // constructing anything: the new window's own startup resets this, as does the quick window
            // hiding below. Every route from the quick window to the full one comes through here.
            var restorePreview = QuickLookManager.Instance.IsPreviewWanted;
            AppWindowManager.ShowSearchWindowFromQuick(currentSearchText, restorePreview);
            onHideWindow?.Invoke();
            return;
        }

        // Web-address (http/https) favorites: hand straight to the default browser. No filesystem I/O
        // here, but UseShellExecute still means ShellExecuteEx, which resolves the protocol handler and
        // may be starting a cold browser -- no reason for the UI thread to wait on any of that.
        if (Helpers.FavoriteUrlHelper.IsWebUrl(path))
        {
            ShellThread.Run("UrlLaunch", () =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Logger.Log($"[FileExecutor] OpenFileOrFolder failed for '{path}': {ex}", LogLevel.Error);
                    MessageBox.Show(string.Format(TranslationManager.Instance["Executor_OpenFailed"], ex.Message), TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            return;
        }

        path = Environment.ExpandEnvironmentVariables(path);

        // Everything below can block for seconds on a slow or heavily-indexed network share
        // (File.Exists/Directory.Exists have no timeout) -- run it off the UI thread so launching
        // something doesn't freeze the whole app while a background scan is hammering the same share.
        // CustomMessageBox.Show already marshals itself back when called off-thread.
        //
        // On a ShellThread rather than the pool: Process.Start with UseShellExecute is ShellExecuteEx,
        // which delegates to whatever shell extension handles the target, and some of those require an
        // STA. The pool is MTA.
        ShellThread.Run("FileLaunch", () => LaunchExistingPath(path, asAdmin));
    }

    private static void LaunchExistingPath(string path, bool asAdmin)
    {
        try
        {
            // Favorite targets are stored raw (e.g. %USERPROFILE%\Desktop); expand variables before
            // the File.Exists/Directory.Exists checks below so those paths launch correctly.
            path = Environment.ExpandEnvironmentVariables(path);

            var isVirtual = IsVirtualPath(path);
            if (isVirtual || File.Exists(path) || Directory.Exists(path))
            {
                var isFile = !isVirtual && File.Exists(path);
                var defaultFileManager = UserSettings.Load().DefaultFileManager;

                // "Open" remains a normal shell launch for files, while native Explorer folders use the
                // same new-tab route as "Locate". A configured file manager is intentionally allowed to
                // keep its own folder-opening behavior.
                if (!asAdmin && !isVirtual && !isFile && defaultFileManager.OpenFoldersInNewExplorerTabs &&
                    (defaultFileManager is not { Enabled: true } || string.IsNullOrWhiteSpace(defaultFileManager.Path)) &&
                    TryOpenFolderInNewExplorerTab(path, defaultFileManager))
                    return;

                // The "runas" verb applies to executables, not documents, so a non-executable file can't
                // just be elevated directly -- BuildStartInfo below resolves the file's associated program
                // (e.g. Notepad++) and elevates THAT with the file as its argument instead, so admin-open
                // uses the same handler as a normal open. Only resolved when that branch will actually be
                // taken, since it's a real (if cheap) registry/shell lookup.
                var associatedExe = (asAdmin && isFile && !IsElevatableExecutable(path)) ? TryGetAssociatedExecutable(path) : null;
                var startInfo = BuildStartInfo(path, isFile, asAdmin, associatedExe, defaultFileManager);

                if (isFile && !asAdmin)
                {
                    var workingDirectory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(workingDirectory))
                    {
                        if (Directory.Exists(workingDirectory))
                        {
                            startInfo.WorkingDirectory = workingDirectory;
                        }
                    }
                }

                try
                {
                    Process.Start(startInfo);
                }

                catch (Exception startEx)
                {
                    Logger.Log($"[FileExecutor] Process.Start failed for '{path}': {startEx.Message}", LogLevel.Error);
                    throw;
                }
            }

            else
            {
                MessageBox.Show(string.Format(TranslationManager.Instance["Executor_NotExist"], path), TranslationManager.Instance["Executor_PromptTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        catch (Exception ex)
        {
            Logger.Log($"[FileExecutor] OpenFileOrFolder failed for '{path}': {ex}", LogLevel.Error);
            MessageBox.Show(string.Format(TranslationManager.Instance["Executor_OpenFailed"], ex.Message), TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool TryOpenFolderInNewExplorerTab(string path, DefaultFileManagerSetting defaultFileManager) => TryUseExplorerTabs(
            () => ExplorerTabLocator.TryOpenFolderInNewTab(path),
            () =>
            {
                // Keep the first folder on the documented shell route. Once its window is ready, later
                // folders from the same multi-selection enter it as tabs instead of opening N windows.
                Process.Start(BuildStartInfo(path, isFile: false, asAdmin: false, associatedExe: null, defaultFileManager));
                return true;
            });

    // Both folder opening routes share this exact sequence. The location route supplies a first-window
    // callback that uses SHOpenFolderAndSelectItems so Windows can select the requested item.
    private static bool TryUseExplorerTabs(Func<bool> openNewTab, Func<bool>? openFirstWindow)
    {
        FolderOpenGate.Wait();
        try
        {
            if (ExplorerTabLocator.HasAvailableExplorerWindow()) return openNewTab();
            if (openFirstWindow == null || !openFirstWindow()) return false;
            ExplorerTabLocator.WaitForAvailableExplorerWindow();
            return true;
        }
        finally
        {
            FolderOpenGate.Release();
        }
    }

    // Keep "open" and "open containing folder" on the same serialized Explorer-tab route. The
    // location caller supplies the documented first-window operation so it can select the item.
    internal static bool TryLocateInNewExplorerTab(string path, Func<bool>? openFirstWindow = null, IntPtr preferredExplorerWindow = default) =>
        TryUseExplorerTabs(() => ExplorerTabLocator.TryLocateInNewTab(path, preferredExplorerWindow), openFirstWindow);

    // A "::{CLSID}"/"shell:..." token names a virtual shell namespace item (e.g. Control Panel, This PC)
    // rather than a real filesystem path -- File.Exists/Directory.Exists would just return false for it.
    internal static bool IsVirtualPath(string path) =>
        path.StartsWith("::") || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);

    // Extensions the shell will actually elevate directly via the "runas" verb; anything else is a
    // document, which needs its associated program elevated instead (see BuildStartInfo).
    internal static bool IsElevatableExecutable(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".exe" or ".bat" or ".cmd" or ".com" or ".scr" or ".msi" or ".lnk";
    }

    // Pure: decides what to launch and how, given facts about the path already resolved by the caller
    // via real I/O (isFile) and a real registry/shell lookup (associatedExe, only needed for the
    // admin-elevate-a-document branch). Only builds the ProcessStartInfo -- never starts it, and never
    // sets WorkingDirectory (the caller applies that separately, since it's real Directory.Exists I/O
    // that's also non-admin-only, unlike everything decided here).
    internal static ProcessStartInfo BuildStartInfo(string path, bool isFile, bool asAdmin, string? associatedExe, DefaultFileManagerSetting? defaultFileManager = null)
    {
        if (!asAdmin)
        {
            // A user-configured default file manager (see GitHub issue #180) only ever applies to
            // opening a FOLDER -- a file still needs its own associated program, not the file manager.
            if (!isFile && TryBuildDefaultFileManagerStartInfo(path, defaultFileManager) is { } customStartInfo)
                return customStartInfo;
            return new ProcessStartInfo { FileName = path, UseShellExecute = true };
        }

        if (!isFile)
            return new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/k cd /d \"{path}\"", UseShellExecute = true, Verb = "runas" };

        if (IsElevatableExecutable(path))
            return new ProcessStartInfo { FileName = path, UseShellExecute = true, Verb = "runas" };

        if (!string.IsNullOrEmpty(associatedExe))
            return new ProcessStartInfo { FileName = associatedExe, Arguments = $"\"{path}\"", UseShellExecute = true, Verb = "runas" };

        // No association resolved — bring up the shell "Open with" dialog, but run it ELEVATED (runas).
        // The program the user then picks is launched as a child of the elevated dialog and inherits
        // admin rights, which matches the admin-open intent instead of degrading to a normal launch.
        // OpenWith.exe is a normal exe that pops the same "Open with" dialog and takes a standard quoted
        // path argument (so spaces just work). Elevating it means the program the user picks inherits
        // admin rights.
        return new ProcessStartInfo { FileName = "OpenWith.exe", Arguments = $"\"{path}\"", UseShellExecute = true, Verb = "runas" };
    }

    // Null when no custom manager is configured, so a disabled/empty setting never accidentally
    // launches anything -- callers keep whatever their own default behavior already is. Used both here
    // (plain "open a folder") and by ExplorerLocateHelper ("open containing folder"/Ctrl+Enter), per
    // GitHub issue #180: one generic method rather than teaching each caller about the setting itself.
    internal static ProcessStartInfo? TryBuildDefaultFileManagerStartInfo(string folderPath, DefaultFileManagerSetting? setting)
    {
        if (setting is not { Enabled: true } || string.IsNullOrWhiteSpace(setting.Path)) return null;
        return new ProcessStartInfo { FileName = setting.Path, Arguments = BuildDefaultFileManagerArguments(folderPath, setting.Parameter), UseShellExecute = true };
    }

    // "%s" and "{}" both expand to the quoted folder path -- same two interchangeable placeholders (and
    // the same ArgQuoting.Quote) as CustomActions.DynamicActionProvider.RunMulti already uses, so this
    // setting works exactly the way that one already does. The user must NOT wrap the placeholder in
    // their own quotes, since that would double up. An empty template just passes the quoted path as the
    // sole argument.
    internal static string BuildDefaultFileManagerArguments(string folderPath, string? parameterTemplate)
    {
        var quotedPath = ArgQuoting.Quote(folderPath);
        return string.IsNullOrWhiteSpace(parameterTemplate) ? quotedPath : parameterTemplate.Replace("%s", quotedPath).Replace("{}", quotedPath);
    }

    public static void LocateInExplorer(string path) => ExplorerLocateHelper.LocateInExplorer(path);

    public static bool TryLocateInExistingExplorer(string path, IntPtr explorerHwnd) => ExplorerLocateHelper.TryLocateInExistingExplorer(path, explorerHwnd);

    private enum AssocStr { Executable = 2 }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, EntryPoint = "AssocQueryStringW")]
    private static extern int AssocQueryString(uint flags, AssocStr str, string pszAssoc, string? pszExtra, System.Text.StringBuilder? pszOut, ref uint pcchOut);

    /// <summary>
    /// Resolves the executable associated with a file's extension (the program a normal double-click
    /// would launch). Returns null when there is no real association, so callers can fall back.
    /// </summary>
    private static string? TryGetAssociatedExecutable(string path)
    {
        try
        {
            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
                return null;

            uint length = 1024;
            var sb = new System.Text.StringBuilder((int)length);
            if (AssocQueryString(0, AssocStr.Executable, ext, null, sb, ref length) != 0) // S_OK == 0
                return null;

            var exe = sb.ToString();
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return null;

            // Windows hands back a generic launcher when there is no real handler; don't elevate those.
            var name = Path.GetFileName(exe).ToLowerInvariant();
            if (name is "openwith.exe" or "rundll32.exe" or "applicationframehost.exe")
                return null;

            return exe;
        }
        catch
        {
            return null;
        }
    }
}
