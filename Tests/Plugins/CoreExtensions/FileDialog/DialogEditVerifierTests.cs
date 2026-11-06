using System.Runtime.InteropServices;

using Lertaro.Plugins.CoreExtensions.FileDialog;

namespace Lertaro.Plugins.CoreExtensions.Tests.FileDialog;

// The read-back is the only answer available to a sender whose write UIPI threw away, so it is pinned against
// a real edit control: an assertion about what the control itself holds cannot be satisfied by the value the
// caller meant. Everything here runs in this process at this process's integrity, which is also why the
// negative case is asserted on the comparison, not on a window -- a same-integrity edit always accepts the
// text it is handed, and no lower-integrity target can be conjured in a test.
[TestClass]
public sealed class DialogEditVerifierTests
{
    [TestMethod]
    public void AControlThatNeverHeardThePathIsNotAMatch()
    {
        Assert.IsFalse(DialogEditVerifier.Matches(null, @"C:\Reports\report.pdf"));
        Assert.IsFalse(DialogEditVerifier.Matches(string.Empty, @"C:\Reports\report.pdf"));
        Assert.IsFalse(DialogEditVerifier.Matches(@"C:\Windows", @"C:\Reports\report.pdf"));
    }

    [TestMethod]
    public void ASeparatorAndADriveLetterSpellingAreNotADifference()
    {
        // The adapters add the trailing separator to a directory themselves, and a dialog answers with
        // whichever spelling of the drive the user typed.
        Assert.IsTrue(DialogEditVerifier.Matches(@"C:\Reports", @"C:\Reports\"));
        Assert.IsTrue(DialogEditVerifier.Matches(@"c:\reports\", @"C:\Reports\"));
    }

    [TestMethod]
    public void TheControlAnswersWithWhatItActuallyHolds()
    {
        var edit = CreateEdit(IntPtr.Zero);
        var path = @"C:\Reports\2026" + '\\';
        try
        {
            SendMessage(edit, WM_SETTEXT, IntPtr.Zero, path);

            Assert.AreEqual(path, DialogEditVerifier.ReadText(edit));
            Assert.IsTrue(DialogEditVerifier.Matches(DialogEditVerifier.ReadText(edit), path));
            Assert.IsFalse(DialogEditVerifier.Matches(DialogEditVerifier.ReadText(edit), @"D:\Other"));
        }
        finally
        {
            DestroyWindow(edit);
        }
    }

    // The navigation the change is meant to keep working: a dialog whose name field takes the path must still
    // be reported as navigated, or every ordinary Open/Save would start looking like a failure.
    [TestMethod]
    public void TheClassicAdapterNavigatesANameFieldThatAcceptedThePath()
    {
        var frame = CreateWindow("STATIC", IntPtr.Zero, WS_POPUP);
        var edit = CreateEdit(frame);
        try
        {
            var target = @"C:\Lertaro-NoSuch-Path";
            Assert.IsTrue(new ClassicFileDialogAdapter().NavigateTo(frame, target));
            Assert.AreEqual(target, DialogEditVerifier.ReadText(edit));
        }
        finally
        {
            DestroyWindow(edit);
            DestroyWindow(frame);
        }
    }

    private static IntPtr CreateEdit(IntPtr parent) =>
        CreateWindowEx(0, "EDIT", null, parent == IntPtr.Zero ? WS_POPUP : WS_CHILD,
            0, 0, 80, 20, parent, IntPtr.Zero, ModuleHandle, IntPtr.Zero);

    private static IntPtr CreateWindow(string className, IntPtr parent, int style) =>
        CreateWindowEx(0, className, null, style, 0, 0, 8, 8, parent, IntPtr.Zero, ModuleHandle, IntPtr.Zero);

    private static IntPtr ModuleHandle => GetModuleHandle(null);

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_CHILD = 0x40000000;
    private const uint WM_SETTEXT = 0x000C;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string? lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);
}
