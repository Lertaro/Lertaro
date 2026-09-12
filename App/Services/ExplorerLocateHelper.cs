using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Lertaro.Core;
using Lertaro.Core.Hook;
using Lertaro.PluginSdk.Helpers;
using MessageBox = Lertaro.App.Views.Controls.Dialogs.CustomMessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace Lertaro.App.Services;

// "Select this item in Explorer" -- split out of FileExecutor to keep that file under the line-count
// limit. Routes through the shell (SHOpenFolderAndSelectItems / an existing window's own Navigate2) so
// it respects the user's default file manager instead of always opening explorer.exe.
internal static class ExplorerLocateHelper
{
    private const string ExplorerTabClass = "ShellTabWindowClass";
    private static readonly Guid ShellBrowserService = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid ShellBrowserInterface = new("000214E2-0000-0000-C000-000000000046");

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
        // deliberately left virtual here rather than resolved like FileExecutor's own callers do:
        // SHParseDisplayName further down takes the token as-is, and Path.GetDirectoryName("shell:...")
        // is empty, which is exactly what routes a virtual item to the shell-locate fallback.
        path = UserPathResolver.Expand(path);

        // A user-configured default file manager (see GitHub issue #180, FileExecutor.
        // TryBuildDefaultFileManagerStartInfo) takes over "open containing folder" too -- it can only open
        // the folder itself, not select-and-highlight the specific item within it the way
        // SHOpenFolderAndSelectItems below does, since there's no generic way to know a third-party tool's
        // own "select this item" argument syntax. Accepted tradeoff: one generic open-folder method
        // reused everywhere, rather than each caller needing its own opinion about the setting.
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
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

        if (fileManager.OpenFoldersInNewExplorerTabs && FileExecutor.TryLocateInNewExplorerTab(path, () => TryLocateWithShell(path)))
            return;

        if (TryLocateWithShell(path)) return;

        // Fallback
        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            Logger.Log($"[FileExecutor] Locate in explorer failed for '{path}': {ex.Message}", LogLevel.Error);
            MessageBox.Show(string.Format(TranslationManager.Instance["Executor_LocateFailed"], ex.Message), TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[]? apidl, uint dwFlags);

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
        dynamic? window = null;
        try
        {
            window = FindExplorerWindow(explorerHwnd);
            if (window == null) return false;

            // The parent, whatever the item is. Navigating to the item itself when it happened to be a
            // folder made "open containing folder" step INTO that folder and select nothing -- which is
            // just what "open" does, and not what the action says. A drive root has no parent to show,
            // so it falls through to LocateInExplorer rather than pretending it worked.
            var targetFolder = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
            {
                return false;
            }

            window.Navigate2(targetFolder);
            // Folders get selected too, for the same reason: the gate used to be File.Exists, so a
            // located folder was never highlighted once the window arrived.
            SelectItemInExplorer(path, explorerHwnd);

            return true;
        }

        catch (Exception ex)
        {
            Logger.Log($"[FileExecutor] Locate in existing explorer failed for '{path}': {ex.Message}", LogLevel.Error);
            return false;
        }
        finally
        {
            ReleaseComObject(window);
        }
    }

    private static dynamic? FindExplorerWindow(IntPtr explorerHwnd)
    {
        var activeTabHwnd = FindActiveTab(explorerHwnd);
        object? shellWindows = null;
        try
        {
            var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
            if (shellWindowsType == null) return null;
            shellWindows = Activator.CreateInstance(shellWindowsType);
            if (shellWindows == null) return null;

            dynamic dShellWindows = shellWindows;
            int count = dShellWindows.Count;
            object? match = null;
            for (var i = 0; i < count; i++)
            {
                object? window = null;
                try
                {
                    window = dShellWindows.Item(i);
                    if (window == null) continue;
                    dynamic dWindow = window;
                    if ((IntPtr)dWindow.HWND == explorerHwnd)
                    {
                        // ShellWindows exposes one COM item per Explorer tab, but all items can carry
                        // the same top-level HWND. Matching only that HWND therefore returns the first
                        // tab in enumeration order instead of the tab the user is currently viewing.
                        // Resolve the candidate's IShellBrowser tab HWND and require it to match the
                        // active ShellTabWindowClass child before accepting the item.
                        if (activeTabHwnd != IntPtr.Zero &&
                            (!TryGetTabHandle(window, out var candidateTabHwnd) || candidateTabHwnd != activeTabHwnd))
                        {
                            continue;
                        }

                        match = window;
                        window = null;
                        break;
                    }
                }
                catch { }
                finally
                {
                    ReleaseComObject(window);
                }
            }

            return match;
        }
        finally
        {
            ReleaseComObject(shellWindows);
        }
    }

    private static IntPtr FindActiveTab(IntPtr explorerHwnd)
    {
        var activeTabHwnd = IntPtr.Zero;
        EnumChildWindows(explorerHwnd, (childHwnd, _) =>
        {
            var className = new StringBuilder(64);
            ExplorerNativeHooks.GetClassName(childHwnd, className, className.Capacity);
            if (className.ToString().Equals(ExplorerTabClass, StringComparison.OrdinalIgnoreCase))
            {
                activeTabHwnd = childHwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return activeTabHwnd;
    }

    private static bool TryGetTabHandle(object window, out IntPtr tabHwnd)
    {
        tabHwnd = IntPtr.Zero;
        if (window is not IComServiceProvider serviceProvider)
        {
            return false;
        }

        var serviceGuid = ShellBrowserService;
        var interfaceGuid = ShellBrowserInterface;
        if (serviceProvider.QueryService(ref serviceGuid, ref interfaceGuid, out var shellBrowserPtr) != 0 ||
            shellBrowserPtr == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var shellBrowser = (IShellBrowser)Marshal.GetObjectForIUnknown(shellBrowserPtr);
            try
            {
                return shellBrowser.GetWindow(out tabHwnd) == 0 && tabHwnd != IntPtr.Zero;
            }
            finally
            {
                Marshal.ReleaseComObject(shellBrowser);
            }
        }
        finally
        {
            Marshal.Release(shellBrowserPtr);
        }
    }

    private static bool TryLocateWithShell(string path)
    {
        var pidl = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _) != 0) return false;
            SHOpenFolderAndSelectItems(pidl, 0, null, 0);
            return true;
        }
        catch { return false; }
        finally
        {
            if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
        }
    }

    // Runs on the caller's ShellThread STA and blocks for a beat before selecting: Navigate2 needs a
    // moment before the item exists to select, and staying on the SAME thread keeps every COM call on
    // the STA the RCWs were created on. This used to be an async-void helper resuming on the thread
    // pool after Task.Delay -- by then the one-shot STA thread had exited, and COM calls against its
    // dead apartment failed with RPC_E_DISCONNECTED, so the selection silently never happened.
    // Blocking the ShellThread for 250ms is fine: it is a thread-per-call worker built for exactly
    // this kind of blocking shell work.
    private static void SelectItemInExplorer(string path, IntPtr explorerHwnd)
    {
        Thread.Sleep(250);

        dynamic? window = null;
        dynamic? folder = null;
        object? item = null;
        try
        {
            window = FindExplorerWindow(explorerHwnd);
            if (window == null) return;
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) return;
            folder = window.Document.Folder;
            if (folder == null) return;
            item = folder.ParseName(name);
            if (item == null) return;
            const int svsiSelect = 0x1;
            const int svsiDeselectOthers = 0x4;
            const int svsiEnsureVisible = 0x8;
            window.Document.SelectItem(item, svsiSelect | svsiDeselectOthers | svsiEnsureVisible);
        }

        catch (Exception ex)
        {
            Logger.Log($"[FileExecutor] Select item in existing explorer failed for '{path}': {ex.Message}", LogLevel.Error);
        }
        finally
        {
            ReleaseComObject(item);
            ReleaseComObject(folder);
            ReleaseComObject(window);
        }
    }

    private static void ReleaseComObject(object? comObject)
    {
        try
        {
            if (comObject != null && Marshal.IsComObject(comObject))
                Marshal.ReleaseComObject(comObject);
        }
        catch
        {
            // Best-effort cleanup; the RCW will still be reclaimed by the GC finalizer.
        }
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    private interface IComServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid serviceGuid, ref Guid interfaceGuid, out IntPtr service);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    private interface IShellBrowser
    {
        [PreserveSig]
        int GetWindow(out IntPtr hwnd);
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc callback, IntPtr lParam);
}
