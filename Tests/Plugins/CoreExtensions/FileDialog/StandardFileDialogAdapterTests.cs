using System.Runtime.InteropServices;
using Lertaro.Plugins.CoreExtensions.FileDialog;

namespace Lertaro.Plugins.CoreExtensions.Tests.FileDialog;

// This probe decides whether the inline card hides files, so it is pinned against the control shapes measured
// off live dialogs: an Open/Save dialog carries the shell's file-name ComboBoxEx32 #1148 (whose own child Edit
// repeats that same id), a FOS_PICKFOLDERS picker has no #1148 at all and puts a plain Edit #1152 in that slot.
// Real windows, because the probe reads real Win32 class names and dialog control ids.
[TestClass]
public sealed class StandardFileDialogAdapterTests
{
    [TestMethod]
    public void TheOpenDialogShapeTakesFiles()
    {
        var adapter = new StandardFileDialogAdapter();
        using var dialog = FakeCommonDialog.WithNameCombo();

        Assert.IsTrue(adapter.CanHandle(dialog.Hwnd, "#32770", "notepad"));
        Assert.IsFalse(adapter.TargetIsFolderOnly);
    }

    [TestMethod]
    public void TheBrowseForFolderShapeTakesOnlyFolders()
    {
        var adapter = new StandardFileDialogAdapter();
        using var dialog = FakeCommonDialog.WithFolderEdit();

        Assert.IsTrue(adapter.CanHandle(dialog.Hwnd, "#32770", "someapp"));
        Assert.IsTrue(adapter.TargetIsFolderOnly);
    }

    // The probe asks both halves of its question on purpose: a frame that has no #1148 to begin with must not
    // be guessed at, because guessing here is what hides every file from someone trying to pick one.
    [TestMethod]
    public void AFrameWithNeitherControlIsNotGuessedAt()
    {
        var adapter = new StandardFileDialogAdapter();
        using var dialog = FakeCommonDialog.WithNeitherNameField();

        Assert.IsTrue(adapter.CanHandle(dialog.Hwnd, "#32770", "someapp"));
        Assert.IsFalse(adapter.TargetIsFolderOnly);
    }

    private const int NameFieldId = 1148;
    private const int FolderEditId = 1152;

    // The shell dialog's control layout, built in this process and destroyed afterwards. The frame itself is a
    // plain static: CanHandle is handed the class name as text, and only the children are read back from Win32.
    private sealed class FakeCommonDialog : IDisposable
    {
        public IntPtr Hwnd { get; }

        private FakeCommonDialog(IntPtr hwnd) => Hwnd = hwnd;

        public static FakeCommonDialog WithNameCombo() => new(Build(nameCombo: true, folderEdit: false));

        public static FakeCommonDialog WithFolderEdit() => new(Build(nameCombo: false, folderEdit: true));

        public static FakeCommonDialog WithNeitherNameField() => new(Build(nameCombo: false, folderEdit: false));

        private static IntPtr Build(bool nameCombo, bool folderEdit)
        {
            RegisterHelperClasses();
            var hwnd = CreateWindow("STATIC", IntPtr.Zero, WS_POPUP);
            Assert.AreNotEqual(IntPtr.Zero, hwnd, "no dialog frame to ask, so the assertions below would prove nothing");
            // CanHandle refuses anything without the address bar, so every shape needs it.
            CreateChild("Breadcrumb Parent", hwnd, 0);
            if (nameCombo) CreateChild("ComboBoxEx32", hwnd, NameFieldId);
            if (folderEdit) CreateChild("Edit", hwnd, FolderEditId);
            return hwnd;
        }

        public void Dispose()
        {
            if (Hwnd != IntPtr.Zero) DestroyWindow(Hwnd);
        }
    }

    // "Breadcrumb Parent" is not registered until some shell dialog has existed in the process, and a test
    // thread cannot rely on that, so it is registered here. Where a class already exists -- "ComboBoxEx32"
    // comes with comctl32 -- RegisterClassEx fails and the existing one is found by name instead, which is all
    // the probe looks at.
    private static void RegisterHelperClasses()
    {
        RegisterClass("Breadcrumb Parent");
        RegisterClass("ComboBoxEx32");
    }

    private static void RegisterClass(string className)
    {
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = DefWindowProc,
            hInstance = ModuleHandle,
            lpszClassName = className,
        };
        RegisterClassEx(ref wc);
    }

    private static void CreateChild(string className, IntPtr parent, int controlId) =>
        Assert.AreNotEqual(IntPtr.Zero, CreateWindow(className, parent, WS_CHILD, controlId),
            $"child window {className} could not be created");

    private static IntPtr CreateWindow(string className, IntPtr parent, int style, int controlId = 0) =>
        CreateWindowEx(0, className, null, style, 0, 0, 8, 8, parent, new IntPtr(controlId), ModuleHandle, IntPtr.Zero);

    private static IntPtr ModuleHandle => GetModuleHandle(null);

    private static readonly IntPtr DefWindowProc = GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW");

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_CHILD = 0x40000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string? lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
}
