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

    private static readonly Dictionary<IntPtr, string> _lastActiveSides = new Dictionary<IntPtr, string>();

    public string? TryGetPath(IntPtr activeHwnd, string activeClassName, IntPtr windowHwnd, string windowClassName, string processName)
    {
        if (windowHwnd == IntPtr.Zero) return null;

        CleanUpDeadKeys();

        var containers = Win32Helper.GetVisibleContainers(windowHwnd);
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
                    _lastActiveSides[windowHwnd] = activeIndex.ToString();
                }
                activeContainer = containers[activeIndex];
            }
            else
            {
                string lastSideIndexStr;
                lock (_lastActiveSides)
                {
                    if (!_lastActiveSides.TryGetValue(windowHwnd, out lastSideIndexStr!))
                    {
                        lastSideIndexStr = "0";
                    }
                }

                if (int.TryParse(lastSideIndexStr, out var targetIndex) && targetIndex < containers.Count)
                {
                    activeContainer = containers[targetIndex];
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

    private string? ExtractPathFromContainer(IntPtr containerHwnd)
    {
        var locationBar = Win32Helper.FindWindowExRecursively(containerHwnd, IntPtr.Zero, "dopus.ctl.treepath", null);
        if (locationBar != IntPtr.Zero)
        {
            var path = Win32Helper.GetWindowText(locationBar);
            return ResolveAndVerify(path);
        }
        return null;
    }

    private string? ResolveAndVerify(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var resolved = ShellPathHelper.ResolveSpecialFolder(path);
        if (resolved.Length == 2 && resolved[1] == ':')
        {
            resolved += "\\";
        }
        if (!string.IsNullOrEmpty(resolved))
        {
            if (Path.IsPathRooted(resolved) && (resolved.Contains(":\\") || resolved.StartsWith("\\\\")))
            {
                return resolved;
            }
            if (Directory.Exists(resolved))
            {
                return resolved;
            }
        }
        return null;
    }

    private static void CleanUpDeadKeys()
    {
        lock (_lastActiveSides)
        {
            var deadKeys = new List<IntPtr>();
            foreach (var key in _lastActiveSides.Keys)
            {
                if (!Win32Helper.IsWindow(key))
                {
                    deadKeys.Add(key);
                }
            }
            foreach (var key in deadKeys)
            {
                _lastActiveSides.Remove(key);
            }
        }
    }
}
