using System.IO;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.Xplorer2.Win32;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.Xplorer2;

/// <summary>
/// Inline-search integration for xplorer2 (ATL:ExplorerFrame). The current folder is read from its
/// address bar over WM_GETTEXT and a jump is committed by driving that same address bar, because
/// xplorer2 exposes no scripting interface to outside processes (see Win32Helper for what was measured).
/// </summary>
public class Xplorer2InlineSearchAdapter : IInlineSearchAdapter
{
    public string Name => "xplorer2";

    public bool IsFileExplorer => true;

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (!PluginSettingsService.GetSetting("Lertaro.Plugins.Xplorer2", "EnableInlineSearch", true))
            return false;

        return CanRecognizeHost(hwnd, className, processName);
    }

    public bool CanRecognizeHost(IntPtr hwnd, string className, string processName)
    {
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(processName))
            return false;

        return processName.StartsWith("xplorer2", StringComparison.OrdinalIgnoreCase) &&
               className.Equals(WindowProbe.FrameClass, StringComparison.OrdinalIgnoreCase);
    }

    // Gated on the pane's own container, not just the class: xplorer2's mini-scrap bin is a SysListView32
    // too, and opening the card over it would search a folder the user is not looking at.
    public bool CanTrigger(IntPtr focusedHwnd, string className)
    {
        if (focusedHwnd == IntPtr.Zero || string.IsNullOrEmpty(className)) return false;
        return Win32Helper.IsFileList(focusedHwnd, className);
    }

    public bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor)
    {
        if (!PluginSettingsService.GetSetting("Lertaro.Plugins.Xplorer2", "EnableQuickNav", true))
            return false;

        return CanTrigger(hwndUnderCursor, classNameUnderCursor);
    }

    public string? GetSearchScope(IntPtr hwnd) => Win32Helper.QueryActivePath(hwnd);

    public bool ExecuteItem(IntPtr hwnd, string path, string searchInput)
    {
        try
        {
            // Don't probe the filesystem to tell a folder from a file: this runs in the elevated Hook
            // process, where a mapped drive set up in the user's own session is invisible to
            // Directory.Exists. The caller's trailing separator is the signal (see IInlineSearchAdapter).
            var isDir = Path.EndsInDirectorySeparator(path);
            var cleanPath = isDir ? Path.TrimEndingDirectorySeparator(path) : path;
            return Win32Helper.Navigate(hwnd, cleanPath);
        }
        catch
        {
            return false;
        }
    }

    // No OnSelectionChanged: xplorer2 owns its list selection and ignores externally posted
    // LVM_SETITEMSTATE (measured on every pane: the selected count never moved), and the only other
    // "mirror" verb available would be navigating the address bar on each arrow key. Returning false from
    // LocateItem for the same reason.

    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (!WindowProbe.TryGetDockBounds(hwnd, out var bounds)) return false;
        rect = new AdapterRect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
        return true;
    }

    public bool CanEnterActionsMode(IntPtr hwnd) => true;
}
