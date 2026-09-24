using System.Runtime.InteropServices;
using System.Text;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.PluginSdk.Registries;

public static class FileDialogAdapterRegistry
{
    private static readonly List<IFileDialogAdapter> Adapters = new();

    /// <summary>
    /// Delegate to determine if an adapter is enabled.
    /// </summary>
    public static Func<IFileDialogAdapter, bool> FilterFunc { get; set; } = _ => true;

    // Windows whose dialogs are claimed by the generic detection while being no file dialog at all, keyed
    // by executable name (the same form IFileDialogAdapter.CanHandle receives) to the captions to refuse.
    //
    // Refused here rather than in whichever adapter misfired because every consumer of "is this a file
    // dialog" asks GetMatchingAdapter -- the window tracker that decides whether to pop the inline card,
    // Quick Navigation, the favorites hotkeys, the path poller, the command handler. Loosening one adapter
    // would leave the next in line free to claim the same window, and one consumer then believing the
    // lie is enough to show the card or route a path into a dialog that cannot take one.
    //
    // Keyed on caption as well as process because these apps do open genuine common dialogs too, and the
    // card is wanted there.
    private static readonly Dictionary<string, string[]> NotFileDialogCaptions = new(StringComparer.OrdinalIgnoreCase)
    {
        // CAJViewer's "打印" print dialog, reported by a user as having the inline card pop over it.
        ["CAJVieweru"] = ["打印"]
    };

    public static void Register(IFileDialogAdapter adapter)
    {
        lock (Adapters)
        {
            if (!Adapters.Contains(adapter))
            {
                Adapters.Add(adapter);
            }
        }
    }

    public static IFileDialogAdapter? GetMatchingAdapter(IntPtr hwnd, string className, string processName)
    {
        lock (Adapters)
        {
            foreach (var adapter in Adapters)
            {
                if (FilterFunc(adapter) && adapter.CanHandle(hwnd, className, processName))
                {
                    return IsCaptionExcluded(hwnd, processName) ? null : adapter;
                }
            }
        }
        return null;
    }

    // Only reached once some adapter already claimed the window, so the caption read stays off the hot path.
    private static bool IsCaptionExcluded(IntPtr hwnd, string processName)
    {
        if (string.IsNullOrEmpty(processName) || !NotFileDialogCaptions.ContainsKey(processName)) return false;
        var title = new StringBuilder(256);
        GetWindowText(hwnd, title, title.Capacity);
        return IsExcluded(processName, title.ToString());
    }

    internal static bool IsExcluded(string? processName, string? windowTitle)
    {
        if (string.IsNullOrEmpty(processName) || string.IsNullOrEmpty(windowTitle)) return false;
        if (!NotFileDialogCaptions.TryGetValue(processName, out var captions)) return false;

        var title = windowTitle.Trim();
        return Array.Exists(captions, caption => string.Equals(caption, title, StringComparison.OrdinalIgnoreCase));
    }

    // CharSet.Unicode, not Auto: Auto would bind the ANSI entry point on a .NET core process and hand back
    // the caption mangled for anything outside the system codepage -- "打印" included.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    /// <summary>
    /// Retrieves only active (enabled) adapters.
    /// </summary>
    public static IReadOnlyList<IFileDialogAdapter> GetAdapters()
    {
        lock (Adapters)
        {
            var active = new List<IFileDialogAdapter>();
            foreach (var a in Adapters)
            {
                if (FilterFunc(a))
                {
                    active.Add(a);
                }
            }
            return active;
        }
    }

    /// <summary>
    /// Retrieves all registered adapters, regardless of enabled status.
    /// </summary>
    public static IReadOnlyList<IFileDialogAdapter> GetAllAdapters()
    {
        lock (Adapters)
        {
            return Adapters.ToArray();
        }
    }
}
