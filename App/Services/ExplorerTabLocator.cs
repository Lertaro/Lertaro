using System.Diagnostics;
using System.IO;
using Lertaro.Core;

namespace Lertaro.App.Services;

// Opening a folder in a NEW TAB of an Explorer window that is already open. Windows has no public API for
// that, so the tab is requested through UI Automation (ExplorerTabUiAutomation) and then driven through
// its own Shell.Application object (ExplorerShellWindowsHelper). Every step is allowed to fail: the
// caller falls back to the documented shell route, which is what every one of these requests used before
// any of this existed.
internal static class ExplorerTabLocator
{
    // ponytail: Explorer has no stable identifier for a newly created tab, so concurrent requests
    // cannot safely associate their target with the right tab. Serialize them; a public tab API is the upgrade path.
    private static readonly SemaphoreSlim TabOpenGate = new(1, 1);

    public static bool TryLocateInNewTab(string path) => TryLocateInNewTab(path, IntPtr.Zero);

    public static bool TryLocateInNewTab(string path, IntPtr preferredExplorerWindow)
    {
        var targetFolder = Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(targetFolder) && TryOpenInNewTab(targetFolder, Path.GetFileName(path), path, preferredExplorerWindow);
    }

    public static bool TryOpenFolderInNewTab(string path) => TryOpenInNewTab(path, string.Empty, path, IntPtr.Zero);

    private static bool TryOpenInNewTab(string targetFolder, string itemName, string sourcePath, IntPtr preferredExplorerWindow)
    {
        TabOpenGate.Wait();
        try
        {
            var explorerWindow = ExplorerShellWindowsHelper.FindExplorerWindowHandle(preferredExplorerWindow);
            if (explorerWindow == IntPtr.Zero) return false;

            var tabsBefore = ExplorerShellWindowsHelper.GetTabHandles(explorerWindow);
            if (!ExplorerTabUiAutomation.TryOpenNewTab(explorerWindow)) return false;

            var newTab = WaitForNewTab(explorerWindow, tabsBefore);
            if (newTab == IntPtr.Zero) return false;

            var tabExplorer = ExplorerShellWindowsHelper.FindShellWindowForTab(newTab, explorerWindow, waitForMatch: true);
            if (tabExplorer == null) return false;

            try
            {
                ExplorerShellWindowsHelper.NavigateAndSelect(tabExplorer, targetFolder, itemName);
                return true;
            }
            finally
            {
                ExplorerShellWindowsHelper.ReleaseComObject(tabExplorer);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[ExplorerTabLocator] New-tab locate failed for '{sourcePath}': {ex.Message}", LogLevel.Error);
            return false;
        }
        finally
        {
            TabOpenGate.Release();
        }
    }

    public static bool HasAvailableExplorerWindow() => ExplorerShellWindowsHelper.FindExplorerWindowHandle(IntPtr.Zero) != IntPtr.Zero;

    public static bool WaitForAvailableExplorerWindow()
    {
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (HasAvailableExplorerWindow()) return true;
            Thread.Sleep(50);
        }

        return false;
    }

    /// <summary>
    /// The tab window that was not there before, or <see cref="IntPtr.Zero"/> when nothing new appeared.
    /// </summary>
    /// <remarks>
    /// Pure, and the only part of "which tab did I just create" that can be stated without a live Explorer.
    /// When two tabs appear between the two snapshots -- a second request, or the user's own Ctrl+T -- this
    /// resolves to whichever enumerates first, which is why callers serialize their own requests.
    /// </remarks>
    internal static IntPtr FirstNewTabHandle(IReadOnlySet<IntPtr> tabsBefore, IEnumerable<IntPtr> tabsNow) =>
        tabsNow.FirstOrDefault(tab => !tabsBefore.Contains(tab));

    private static IntPtr WaitForNewTab(IntPtr explorerWindow, HashSet<IntPtr> tabsBefore)
    {
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            var newTab = FirstNewTabHandle(tabsBefore, ExplorerShellWindowsHelper.GetTabHandles(explorerWindow));
            if (newTab != IntPtr.Zero) return newTab;
            Thread.Sleep(50);
        }

        return IntPtr.Zero;
    }
}
