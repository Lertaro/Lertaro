using System.Runtime.InteropServices;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Registries;

namespace Lertaro.PluginSdk.Tests.Registries;

// GetMatchingAdapter is what every consumer asks before treating a window as a file dialog, so a window
// that only looks like one has to be turned down here -- the alternative is a card popping over a print
// dialog, or a picked path being sent into a window that has no field for it.
[TestClass]
public sealed class FileDialogAdapterRegistryTests
{
    [TestMethod]
    [DataRow("CAJVieweru", "打印")]
    [DataRow("CAJVieweru", " 打印 ")]
    [DataRow("CAJVIEWERU", "打印")]
    public void ThePrintDialogIsNotAFileDialog(string processName, string title) =>
        Assert.IsTrue(FileDialogAdapterRegistry.IsExcluded(processName, title));

    [TestMethod]
    [DataRow("CAJVieweru", "打开")]
    [DataRow("CAJVieweru", "打印 属性")]
    [DataRow("CAJVieweru", "另存为")]
    [DataRow("explorer", "打印")]
    [DataRow("wps", "打印")]
    [DataRow("", "打印")]
    [DataRow("CAJVieweru", "")]
    public void EverythingElseStaysAFileDialog(string processName, string title) =>
        // The caption alone must not exclude an app, and the process alone must not exclude that app's
        // real Open/Save dialogs: CAJViewer's own file picker is a genuine common dialog.
        Assert.IsFalse(FileDialogAdapterRegistry.IsExcluded(processName, title));

    [TestMethod]
    public void MissingValuesExcludeNothing()
    {
        // Called straight from the hook threads with whatever the window happened to report.
        Assert.IsFalse(FileDialogAdapterRegistry.IsExcluded(null!, "打印"));
        Assert.IsFalse(FileDialogAdapterRegistry.IsExcluded("CAJVieweru", null!));
    }

    [TestMethod]
    public void AClaimedWindowIsStillRefusedWhenItsCaptionSaysOtherwise()
    {
        // The rule is only worth anything if GetMatchingAdapter consults it after an adapter has already
        // said yes, which is the exact order that produced the report. Real window, real caption, because
        // the read that carries the rule is the Win32 one.
        var adapter = new ClaimedWindowAdapter();
        FileDialogAdapterRegistry.Register(adapter);
        var hwnd = CreateWindowWithCaption("打印");
        try
        {
            adapter.Hwnd = hwnd;
            Assert.IsNull(FileDialogAdapterRegistry.GetMatchingAdapter(hwnd, "#32770", "CAJVieweru"));
            Assert.IsNotNull(FileDialogAdapterRegistry.GetMatchingAdapter(hwnd, "#32770", "Acrobat"));
        }
        finally
        {
            adapter.Hwnd = IntPtr.Zero;
            DestroyWindow(hwnd);
        }
    }

    private static IntPtr CreateWindowWithCaption(string caption)
    {
        var hwnd = CreateWindowEx(0, "STATIC", caption, WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.AreNotEqual(IntPtr.Zero, hwnd, "no window to ask, so the assertion below would prove nothing");
        return hwnd;
    }

    // Registered once for the process and left there (the registry has no unregister): it claims exactly the
    // window handed to it, and Hwnd goes back to zero when the test that needed it is done.
    private sealed class ClaimedWindowAdapter : IFileDialogAdapter
    {
        public IntPtr Hwnd;

        public bool CanHandle(IntPtr hwnd, string className, string processName) => hwnd != IntPtr.Zero && hwnd == Hwnd;

        public string? GetCurrentPath(IntPtr hwnd) => null;

        public bool NavigateTo(IntPtr hwnd, string targetPath) => false;

        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
        {
            rect = default;
            return false;
        }

        public bool RestoreFocus(IntPtr hwnd) => false;
    }

    private const int WS_POPUP = unchecked((int)0x80000000);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
