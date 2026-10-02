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

    public static bool HasAvailableExplorerWindow()
    {
        if (Volatile.Read(ref _tabStripState) == TabStripAbsent) return false;
        return TabBearingWindowSeen();
    }

    public static bool WaitForAvailableExplorerWindow()
    {
        if (Volatile.Read(ref _tabStripState) == TabStripAbsent) return false;

        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (HasAvailableExplorerWindow()) return true;
            Thread.Sleep(50);
        }

        return false;
    }

    /// <summary>
    /// Whether the running shell hosts Explorer's tab strip at all.
    /// </summary>
    /// <remarks>
    /// Windows 10 has no tab strip, so the probe can never see a <c>ShellTabWindowClass</c> there and every
    /// single folder open paid the full two-second wait for a window class that does not exist on that OS --
    /// on a ShellThread worker, holding the serialized folder-open gate while it slept. One honest look is
    /// enough for the rest of the process, and once it says "no tabs" the tab route stops being taken at all
    /// instead of failing at its first step every time.
    /// ponytail: cached for the process lifetime; a shell that gains tabs mid-session (a feature update) has
    /// to be restarted to be noticed.
    /// </remarks>
    internal const int TabStripUnknown = 0;
    internal const int TabStripPresent = 1;
    internal const int TabStripAbsent = 2;
    private static int _tabStripState = TabStripUnknown;

    // The one look at the shell, folded into the cache. Every writer here states a true observation of the
    // same machine state, so a lost update between two threads only decides which true answer to keep.
    private static bool TabBearingWindowSeen()
    {
        var tabBearing = ExplorerShellWindowsHelper.FindExplorerWindowHandle(IntPtr.Zero) != IntPtr.Zero;
        Interlocked.Exchange(ref _tabStripState,
            TabStripStateAfter(Volatile.Read(ref _tabStripState), tabBearing, tabBearing || ExplorerShellWindowsHelper.HasExplorerWindow()));
        return tabBearing;
    }

    /// <summary>
    /// The cache after one look: a tab-bearing window settles it one way, a window without a tab settles it
    /// the other, and no window at all is not evidence.
    /// </summary>
    /// <remarks>
    /// The last case is the one worth keeping honest. A cold Explorer behind a slow share can take longer to
    /// appear than the wait gives it, and latching "this shell has no tabs" on that would switch the feature
    /// off for a Windows 11 shell that does have them.
    /// </remarks>
    internal static int TabStripStateAfter(int previous, bool tabBearingWindow, bool anyExplorerWindow) =>
        tabBearingWindow ? TabStripPresent : anyExplorerWindow ? TabStripAbsent : previous;

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
