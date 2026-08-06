using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using Lertaro.App.ViewModels.QuickPanel;

namespace Lertaro.App.Views.QuickPanel;

// Answers the same WM_COPYDATA request/reply protocol as the Total Commander and XYplorer collectors.
// The path is calculated only for this live window, so closing it cannot leave a stale selection behind.
public partial class QuickPanelWindow
{
    private const int WmCopyData = 0x004A;
    // Must match QuickPanelPathCollector: this is the private protocol between App and the core plugin.
    private const int RequestParentDirectory = 0x534C5150;
    private const int ReplyParentDirectory = 0x534C5151;

    private void AttachPathRequestHandler(IntPtr hwnd) => HwndSource.FromHwnd(hwnd)?.AddHook(PathRequestWindowProc);

    private IntPtr PathRequestWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmCopyData || wParam == IntPtr.Zero)
            return IntPtr.Zero;

        var request = Marshal.PtrToStructure<CopyDataStruct>(lParam);
        if (request.DataType != (IntPtr)RequestParentDirectory)
            return IntPtr.Zero;

        handled = true;
        ReplyWithGroupPath(hwnd, wParam);
        return (IntPtr)1;
    }

    private void ReplyWithGroupPath(IntPtr panelHwnd, IntPtr receiver)
    {
        // FolderPath is the value rendered in the group's Header. It is the panel's current directory
        // regardless of whether the selected tile represents a file or a folder.
        var path = (_activeList?.DataContext as QuickPanelGroupViewModel)?.FolderPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        var bytes = Encoding.Unicode.GetBytes(path + '\0');
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var reply = new CopyDataStruct
            {
                DataType = (IntPtr)ReplyParentDirectory,
                ByteCount = bytes.Length,
                Data = pinned.AddrOfPinnedObject()
            };
            SendMessage(receiver, WmCopyData, panelHwnd, ref reply);
        }
        finally
        {
            pinned.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStruct
    {
        public IntPtr DataType;
        public int ByteCount;
        public IntPtr Data;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, ref CopyDataStruct data);
}
