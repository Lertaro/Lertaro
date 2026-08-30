using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.Plugins.CoreExtensions.Providers.Indexing;
using Lertaro.Plugins.CoreExtensions.Shell.ContextMenu;
namespace Lertaro.Plugins.CoreExtensions.InlineSearch;

public class ExplorerInlineSearchAdapter : IInlineSearchAdapter
{
    public string Name => TranslationService.Get("Plugins_ExplorerTargetName");

    public bool IsFileExplorer => true;

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(processName))
            return false;
        return processName.Equals("explorer", StringComparison.OrdinalIgnoreCase) &&

               (className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase) ||

                className.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||

                className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase));
    }

    public bool CanTrigger(IntPtr focusedHwnd, string className)
    {
        if (focusedHwnd == IntPtr.Zero) return false;
        var current = focusedHwnd;
        var sbClass = new StringBuilder(256);
        while (current != IntPtr.Zero)
        {
            sbClass.Clear();
            ExplorerAdapterHelpers.GetClassName(current, sbClass, sbClass.Capacity);
            var cls = sbClass.ToString();
            if (cls.Equals("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase))
                return true;
            if (cls.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("WorkerW", StringComparison.OrdinalIgnoreCase))
                break;
            current = ExplorerAdapterHelpers.GetParent(current);
        }
        return false;
    }

    public string? GetSearchScope(IntPtr hwnd)
    {
        var sbClass = new StringBuilder(256);
        ExplorerAdapterHelpers.GetClassName(hwnd, sbClass, sbClass.Capacity);
        var className = sbClass.ToString();
        var processName = ExplorerAdapterHelpers.GetProcessName(hwnd);
        var collector = new ExplorerPathCollector();
        return collector.TryGetPath(hwnd, className, hwnd, className, processName);
    }

    public bool ExecuteItem(IntPtr hwnd, string path, string searchInput)
    {
        try
        {
            // The Hook (which runs this) doesn't check Directory.Exists/File.Exists itself -- when it runs
            // elevated (admin auto-elevate), UAC's split token puts it in a different logon session than
            // the one that mapped any network drive letters, so a perfectly valid mapped-drive path would
            // otherwise silently resolve to "doesn't exist". The caller already knows and encodes it as a
            // trailing separator (see InlineAdapterIpcCoordinator.ExecuteItem); stripped back off here so
            // the path passed to Navigate2/ProcessStartInfo is unchanged from before.
            var isDir = Path.EndsInDirectorySeparator(path);
            var cleanPath = isDir ? Path.TrimEndingDirectorySeparator(path) : path;

            var sbClass = new StringBuilder(256);
            ExplorerAdapterHelpers.GetClassName(hwnd, sbClass, sbClass.Capacity);
            var className = sbClass.ToString();

            var isDesktop = className.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                             className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase);

            var alwaysOpen = PluginSettingsService.GetSetting("Lertaro.Plugins.CoreExtensions", "InlineSearchAlwaysOpen", true);

            // The desktop isn't an Explorer pane to navigate within -- acting on an item from there means
            // opening it directly. If InlineSearchAlwaysOpen is enabled, files open directly while folders
            // still navigate in-place in the active Explorer window.
            if (isDesktop || (!isDir && alwaysOpen))
            {
                Process.Start(new ProcessStartInfo { FileName = cleanPath, UseShellExecute = true });
                return true;
            }

            // Land on the item in an existing Explorer window -- navigate into a folder, or navigate to a
            // file's parent and select it -- rather than running it, matching every other file-manager
            // adapter (Total Commander, Directory Opus, XYplorer, ...), none of which ever launch a file
            // here either. TryLocateInExistingExplorer already handles both cases identically (targetFolder
            // = path for a folder, its parent for a file).
            if (TryLocateInExistingExplorer(cleanPath, isDir, hwnd))
            {
                return true;
            }

            if (isDir)
            {
                // No existing window to reuse -- ShellExecute on a folder just opens/navigates into it,
                // same net effect as the locate call above would have had.
                Process.Start(new ProcessStartInfo { FileName = cleanPath, UseShellExecute = true });
                return true;
            }

            // A file must never be launched here -- fall back to the shell's own "open/reuse an Explorer
            // window with this item selected" instead of ProcessStartInfo, which would run it.
            return LocateViaShell(cleanPath);
        }

        catch { }

        return false;
    }

    private static bool LocateViaShell(string path)
    {
        try
        {
            // Reuses the SHParseDisplayName p/invoke already declared in Shell/ShellContextMenuNativeMethods.cs
            // (same project) instead of a second copy here.
            if (ShellContextMenuNativeMethods.SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) == 0)
            {
                SHOpenFolderAndSelectItems(pidl, 0, null, 0);
                Marshal.FreeCoTaskMem(pidl);
                return true;
            }
        }
        catch { }
        return false;
    }

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[]? apidl, uint dwFlags);

    public void OnSelectionChanged(IntPtr hwnd, string path)
    {
        if (hwnd == IntPtr.Zero || string.IsNullOrEmpty(path)) return;
        dynamic? window = null;
        dynamic? folder = null;
        object? item = null;
        try
        {
            window = ExplorerAdapterHelpers.FindExplorerWindow(hwnd);
            if (window == null) return;
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
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
        catch { }
        finally
        {
            ReleaseComObject(item);
            ReleaseComObject(folder);
            ReleaseComObject(window);
        }
    }

    public void OnSearchFinished(IntPtr hwnd, bool executed)
    {
    }

    public IEnumerable<string> GetListItems(IntPtr hwnd)
        => ExplorerInlineSearchListItems.GetListItems(hwnd);

    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero) return false;
        var nativeRect = new ExplorerAdapterHelpers.RECT();
        var result = ExplorerAdapterHelpers.DwmGetWindowAttribute(hwnd, ExplorerAdapterHelpers.DWMWA_EXTENDED_FRAME_BOUNDS, out nativeRect, Marshal.SizeOf<ExplorerAdapterHelpers.RECT>());
        if (result == 0)
        {
            rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
            return true;
        }

        if (ExplorerAdapterHelpers.GetWindowRect(hwnd, out nativeRect))
        {
            rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
            return true;
        }

        return false;
    }

    public bool CanEnterActionsMode(IntPtr hwnd) => true;

    private bool TryLocateInExistingExplorer(string path, bool isDir, IntPtr explorerHwnd)
    {
        if (explorerHwnd == IntPtr.Zero) return false;
        dynamic? window = null;
        try
        {
            window = ExplorerAdapterHelpers.FindExplorerWindow(explorerHwnd);
            if (window == null) return false;
            var targetFolder = isDir ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(targetFolder))
                return false;
            window.Navigate2(targetFolder);
            if (!isDir)
            {
                ExplorerAdapterHelpers.SelectItemInExplorerLater(path, explorerHwnd);
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
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
}
