using System.IO;
using Lertaro.PluginSdk.Helpers;
using Lertaro.Plugins.DirectoryOpus.Win32;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.DirectoryOpus;

public class DirectoryOpusPathCollector : IActivePathCollector
{
    public string Name => "Directory Opus";
    public string TargetName => "Directory Opus";

    public bool CanHandle(string className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        return className.Equals("dopus.lister", StringComparison.OrdinalIgnoreCase);
    }

    // The pane index (into the layout-sorted container list) last known to be active, per lister. Only
    // consulted when no other rule can tell which pane is in front, so a split lister keeps answering with
    // the side the user was last on instead of an arbitrary one.
    private static readonly Dictionary<IntPtr, int> _lastActiveSides = [];

    public string? TryGetPath(IntPtr activeHwnd, string activeClassName, IntPtr windowHwnd, string windowClassName, string processName)
    {
        if (windowHwnd == IntPtr.Zero) return null;

        CleanUpDeadKeys();

        var containers = Win32Helper.GetContainers(windowHwnd, visibleOnly: true);
        if (containers.Count == 0) return null;

        var activeContainer = IntPtr.Zero;

        if (containers.Count == 1)
        {
            activeContainer = containers[0];
        }
        else
        {
            containers.Sort((a, b) =>
            {
                Win32Helper.TryGetWindowRect(a, out var rA);
                Win32Helper.TryGetWindowRect(b, out var rB);
                if (Math.Abs(rA.Left - rB.Left) > 10)
                {
                    return rA.Left.CompareTo(rB.Left);
                }
                return rA.Top.CompareTo(rB.Top);
            });

            var activeIndex = -1;

            var listerTitle = Win32Helper.GetWindowText(windowHwnd);
            if (!string.IsNullOrEmpty(listerTitle))
            {
                var matchCount = 0;
                var lastMatchIdx = -1;
                for (var i = 0; i < containers.Count; i++)
                {
                    var path = ExtractPathFromContainer(containers[i]);
                    if (!string.IsNullOrEmpty(path))
                    {
                        var dirName = Path.GetFileName(path);
                        if (!string.IsNullOrEmpty(dirName) && (
                            listerTitle.Contains(path, StringComparison.OrdinalIgnoreCase) ||
                            listerTitle.StartsWith(dirName + " ", StringComparison.OrdinalIgnoreCase) ||
                            listerTitle.Equals(dirName, StringComparison.OrdinalIgnoreCase)))
                        {
                            matchCount++;
                            lastMatchIdx = i;
                        }
                    }
                }
                if (matchCount == 1)
                {
                    activeIndex = lastMatchIdx;
                }
            }

            if (activeIndex == -1 && activeHwnd != IntPtr.Zero)
            {
                for (var i = 0; i < containers.Count; i++)
                {
                    if (Win32Helper.IsDescendant(containers[i], activeHwnd))
                    {
                        activeIndex = i;
                        break;
                    }
                }

                if (activeIndex == -1 && Win32Helper.TryGetWindowRect(activeHwnd, out var rActive))
                {
                    Win32Helper.TryGetWindowRect(containers[0], out var r0);
                    Win32Helper.TryGetWindowRect(containers[1], out var r1);
                    var isHorizontalSplit = Math.Abs(r0.Left - r1.Left) <= 10;

                    var minDistance = int.MaxValue;
                    for (var i = 0; i < containers.Count; i++)
                    {
                        if (Win32Helper.TryGetWindowRect(containers[i], out var rCont))
                        {
                            var dist = isHorizontalSplit
                                ? Math.Abs(rActive.Top - rCont.Top)
                                : Math.Abs(rActive.Left - rCont.Left);

                            if (dist < minDistance)
                            {
                                minDistance = dist;
                                activeIndex = i;
                            }
                        }
                    }
                }
            }

            if (activeIndex != -1)
            {
                lock (_lastActiveSides)
                {
                    _lastActiveSides[windowHwnd] = activeIndex;
                }
                activeContainer = containers[activeIndex];
            }
            else
            {
                int lastActiveIndex;
                lock (_lastActiveSides)
                {
                    _lastActiveSides.TryGetValue(windowHwnd, out lastActiveIndex);
                }

                if (lastActiveIndex < containers.Count)
                {
                    activeContainer = containers[lastActiveIndex];
                }
            }
        }

        if (activeContainer != IntPtr.Zero)
        {
            var path = ExtractPathFromContainer(activeContainer);
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the folder of every TAB of every visible Directory Opus lister, not just the tab in front,
    /// with each tab group's active tab listed before that group's other tabs.
    /// </summary>
    /// <remarks>
    /// Opus's own interface answers first (see <see cref="DopusRtPathQuery"/>): it reports every tab of
    /// every group along with which one is active, which is what the order needs. Scraping the
    /// file-display windows -- the fallback below -- can only see the tabs whose container is visible and
    /// cannot tell a group's active tab from its neighbours, so it is kept only for the case where Opus
    /// cannot answer at all (not running, tool missing, unexpected output).
    /// </remarks>
    public IReadOnlyList<OpenedFolder> GetOpenedFolders()
    {
        // The documented answer wins whenever it carries folders -- it knows each tab AND which one is
        // active in its group, which the scrape cannot tell.
        var reported = DopusRtPathQuery.TryReadTabs();
        if (reported is { Count: > 0 }) return DopusPathsXml.ToOpenedFolders(reported);

        // But an EMPTY answer is not proof that nothing is open: measured on a live install, Opus answers
        // empty while its file-display containers are right there (a lister it has not finished tracking,
        // a window on another virtual desktop), and trusting it alone left the opened-folder list blank
        // with the folders plainly visible on screen. The scrape is the last word whenever the documented
        // query produced no folders at all -- its answer can be worse ordered, never emptier.
        return GetOpenedFoldersFromWindows();
    }

    private IReadOnlyList<OpenedFolder> GetOpenedFoldersFromWindows()
    {
        var folders = new List<OpenedFolder>();
        foreach (var lister in OpenFolderWindowEnumerator.FindVisibleWindows(IsListerWindow))
        {
            var containers = Win32Helper.GetContainers(lister, visibleOnly: false)
                .Select(container => (Path: ExtractPathFromContainer(container), IsActive: Win32Helper.IsWindowVisible(container), Window: lister));

            folders.AddRange(BuildOpenedFolders(containers));
        }
        return folders;
    }

    /// <summary>
    /// One lister's containers to its opened folders: active tabs first, one entry per distinct folder.
    /// </summary>
    /// <remarks>
    /// Pure, so the policy is pinned without a Directory Opus window to measure. Duplicates are collapsed
    /// only WITHIN a lister -- two tabs showing the same folder are one folder to offer, and the entry's
    /// window handle would be the same lister either way -- while the same folder open in two listers
    /// stays two entries, which is the contract <c>OpenedFolderCollectorRegistry</c> documents for
    /// collectors ("two windows or panes may show the same path, and a consumer can choose whether to
    /// collapse them").
    /// </remarks>
    internal static IReadOnlyList<OpenedFolder> BuildOpenedFolders(
        IEnumerable<(string? Path, bool IsActive, IntPtr Window)> containers)
    {
        var folders = new List<OpenedFolder>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, _, window) in containers.OrderByDescending(container => container.IsActive))
        {
            // The key drops a trailing separator, so two spellings of one folder are one entry: Directory
            // Opus reports a drive root with it and everything else without, and a folder reached both ways
            // is still the same folder to offer. The entry itself keeps the path as reported.
            if (!string.IsNullOrEmpty(path) &&
                seen.Add(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            {
                folders.Add(new OpenedFolder(path, window));
            }
        }

        return folders;
    }

    private string? ExtractPathFromContainer(IntPtr containerHwnd)
    {
        // The location bar exists only on the container of the ACTIVE tab, and reading it first keeps the
        // path reported for the tab in front exactly what it always was. Every other container -- all the
        // inactive tabs -- is read from its own window text, which is where Directory Opus puts the
        // folder for each tab (measured on both states: they agree wherever both exist).
        var locationBar = Win32Helper.FindWindowExRecursively(containerHwnd, IntPtr.Zero, "dopus.ctl.treepath", null);
        var reported = ChooseReportedPath(
            locationBar != IntPtr.Zero ? Win32Helper.GetWindowText(locationBar) : null,
            Win32Helper.GetWindowText(containerHwnd));

        return ResolveReportedPath(reported);
    }

    /// <summary>
    /// The folder a container reports: its location bar when that has text, otherwise the container's own
    /// window text. Pure so the choice can be pinned without a Directory Opus window to measure.
    /// </summary>
    internal static string? ChooseReportedPath(string? locationBarText, string? containerText) =>
        !string.IsNullOrWhiteSpace(locationBarText) ? locationBarText : containerText;

    private static bool IsListerWindow(IntPtr window) =>
        Win32Helper.GetClassName(window).Equals("dopus.lister", StringComparison.OrdinalIgnoreCase);

    // Accepts null because the reported text is now chosen from two controls, either of which can be
    // absent -- an unreadable container is "no path", not an error.
    private string? ResolveReportedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var resolved = ShellPathHelper.ResolveSpecialFolder(path);
        if (resolved.Length == 2 && resolved[1] == ':')
        {
            resolved += "\\";
        }
        if (string.IsNullOrWhiteSpace(resolved)) return null;

        // A container's title is the LOCALIZED spelling Opus shows ("C:\用户\..." for "C:\Users\..."),
        // which is not a path that exists, so the scraped answer has to be translated back before it can
        // be offered. Only reached once the reported spelling is already known not to exist -- the
        // translation walks directories, which every directly-readable path must not pay for.
        return Directory.Exists(resolved) ? resolved : LocalizedPathResolver.Resolve(resolved);
    }

    // A closed lister's handle would otherwise sit in the map for the life of the process, and Windows
    // reuses handles -- a recycled one would answer with a dead lister's pane index.
    private static void CleanUpDeadKeys()
    {
        lock (_lastActiveSides)
        {
            foreach (var key in _lastActiveSides.Keys.Where(key => !Win32Helper.IsWindow(key)).ToArray())
            {
                _lastActiveSides.Remove(key);
            }
        }
    }
}
