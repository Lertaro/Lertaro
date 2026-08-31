using System.Runtime.InteropServices;

namespace Lertaro.Plugins.CoreExtensions.InlineSearch;

// Split out from ExplorerInlineSearchAdapter to keep that file under the repo's per-file line limit.
// Owns the Shell.Application COM enumeration for the adapter's list-items feed and releases every RCW it
// creates, including when the caller stops enumerating early.
internal static class ExplorerInlineSearchListItems
{
    public static IEnumerable<string> GetListItems(IntPtr hwnd)
    {
        var paths = new List<string>();
        object? shellWindows = null;
        try
        {
            var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
            if (shellWindowsType == null) return paths;
            shellWindows = Activator.CreateInstance(shellWindowsType);
            if (shellWindows == null) return paths;
            dynamic dShellWindows = shellWindows;

            var count = 0;
            try { count = dShellWindows.Count; } catch { return paths; }

            for (var i = 0; i < count; i++)
            {
                object? window = null;
                object? folderItems = null;
                try
                {
                    window = dShellWindows.Item(i);
                    if (window == null) continue;
                    dynamic dWindow = window;
                    var windowHwnd = new IntPtr(Convert.ToInt64(dWindow.HWND));
                    if (windowHwnd != hwnd) continue;

                    folderItems = dWindow.Document.Folder.Items();
                    if (folderItems == null) continue;
                    dynamic dFolderItems = folderItems;
                    var itemCount = 0;
                    try { itemCount = dFolderItems.Count; } catch { continue; }

                    for (var j = 0; j < itemCount; j++)
                    {
                        object? fi = null;
                        try
                        {
                            fi = dFolderItems.Item(j);
                            if (fi == null) continue;
                            dynamic dFi = fi;
                            var path = (string)dFi.Path;
                            if (string.IsNullOrWhiteSpace(path)) continue;
                            if (path.StartsWith("::", StringComparison.Ordinal)
                             || path.Contains("::{", StringComparison.Ordinal)
                             || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                                continue;

                            paths.Add(path);
                        }
                        finally
                        {
                            ReleaseComObject(fi);
                        }
                    }
                    break;
                }
                catch { }
                finally
                {
                    ReleaseComObject(folderItems);
                    ReleaseComObject(window);
                }
            }
        }
        finally
        {
            ReleaseComObject(shellWindows);
        }

        return paths;
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
