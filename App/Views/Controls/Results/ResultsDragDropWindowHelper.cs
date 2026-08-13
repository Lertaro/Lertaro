using System.Runtime.InteropServices;
using System.Windows;

namespace Lertaro.App.Views.Controls.Results;

// Split out purely to keep ResultsDragDropHelper under the repo's per-file line limit; this class owns
// only the native cursor/window check and the search-window cleanup performed when an OLE drag ends.
internal static class ResultsDragDropWindowHelper
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public static bool HandleDragEnding()
    {
        if (System.Windows.Application.Current == null || !GetCursorPos(out var mousePosition))
            return false;

        var window = WindowFromPoint(mousePosition);
        uint processId = 0;
        if (window != IntPtr.Zero)
            GetWindowThreadProcessId(window, out processId);
        if (window != IntPtr.Zero && processId == (uint)Environment.ProcessId)
            return true;

        HideSearchWindows();
        return true;
    }

    public static void HideSearchWindows()
    {
        if (System.Windows.Application.Current == null)
            return;

        foreach (var window in System.Windows.Application.Current.Windows.Cast<Window>().ToList())
        {
            if (window is Lertaro.App.QuickSearchWindow quick)
                quick.HideWindow();
            else if (window is Lertaro.App.InlineSearchWindow)
                Services.InlineSearchManager.Instance.CloseInlineSearch("DragDropCompleted");
        }
    }
}
