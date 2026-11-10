using System.Runtime.InteropServices;

namespace Lertaro.Plugins.CoreExtensions.Tests.FileDialog;

// Real Windows dialog frames and a field that can tell whether it was pressed, shared by the common-dialog
// adapters' tests. Both probes read real Win32 class names and dialog control ids, and both adapters post the
// Enter rather than sending it, so a test has to pump messages for it to arrive at all.
internal sealed class FakeCommonDialog : IDisposable
{
    public const int NameFieldId = 1148;
    public const int FolderEditId = 1152;

    public IntPtr Hwnd { get; }

    // The name/folder field NavigateTo writes the path into, when this shape has one.
    public IntPtr Edit { get; }
    private bool _disposed;

    private FakeCommonDialog(IntPtr hwnd, IntPtr folderEdit)
    {
        Hwnd = hwnd;
        Edit = folderEdit;
    }

    public static FakeCommonDialog WithNameCombo() => Create(nameCombo: true, folderEdit: false);

    public static FakeCommonDialog WithFolderEdit() => Create(nameCombo: false, folderEdit: true);

    public static FakeCommonDialog WithNeitherNameField() => Create(nameCombo: false, folderEdit: false);

    public static FakeCommonDialog OwnedCard(IntPtr dialog) =>
        new(CreateWindow("STATIC", dialog, WS_POPUP), IntPtr.Zero);

    public void SetText(string text) => SendMessage(Edit, 0x000C, IntPtr.Zero, text);

    public void SetEnabled(bool enabled) => EnableWindow(Hwnd, enabled);

    // A classic GetOpenFileName/GetSaveFileName frame: the file-name field plus the "files of type" combo the
    // classic adapter's CanHandle insists on -- and no address bar, which is exactly how it tells a classic
    // dialog apart from the modern one.
    public static FakeCommonDialog Classic() => Create(nameCombo: true, folderEdit: false, withCombo: true, withBreadcrumb: false);

    // The same frame with the edit NavigateTo types into.
    public static FakeCommonDialog ClassicWithEdit() => Create(nameCombo: true, folderEdit: true, withCombo: true, withBreadcrumb: false);

    // A modern FOS_PICKFOLDERS picker as the shell draws it: the DirectUI shell view, plus the navigation
    // panes' SysTreeView32 id=100 that the legacy browse dialog also has.
    public static FakeCommonDialog ModernFolderPicker() =>
        Create(nameCombo: false, folderEdit: false, withBreadcrumb: false, withShellView: true, withNavTree: true);

    // A real SHBrowseForFolder: the tree, and nothing else.
    public static FakeCommonDialog LegacyBrowseForFolder() =>
        Create(nameCombo: false, folderEdit: false, withBreadcrumb: false, withNavTree: true);

    private static FakeCommonDialog Create(bool nameCombo, bool folderEdit, bool withCombo = false,
        bool withBreadcrumb = true, bool withShellView = false, bool withNavTree = false)
    {
        RegisterHelperClasses();
        var hwnd = CreateWindow("STATIC", IntPtr.Zero, WS_POPUP);
        Assert.AreNotEqual(IntPtr.Zero, hwnd, "no dialog frame to ask, so the assertions below would prove nothing");
        // Modern dialogs can expose either the address bar or the DirectUI shell view.
        if (withBreadcrumb) CreateChild("Breadcrumb Parent", hwnd, 0);
        if (withShellView) CreateChild("DUIViewWndClassName", hwnd, 0);
        if (withNavTree) CreateChild("SysTreeView32", hwnd, 100);
        if (nameCombo) CreateChild("ComboBoxEx32", hwnd, NameFieldId);
        if (withCombo) CreateChild("ComboBox", hwnd, 0);
        var folderEditHwnd = folderEdit ? CreateChild("Edit", hwnd, FolderEditId) : IntPtr.Zero;
        return new FakeCommonDialog(hwnd, folderEditHwnd);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Hwnd != IntPtr.Zero) DestroyWindow(Hwnd);
    }

    // "Breadcrumb Parent" is not registered until some shell dialog has existed in the process, and a test
    // thread cannot rely on that, so it is registered here. Where a class already exists -- "ComboBoxEx32"
    // comes with comctl32 -- RegisterClassEx fails and the existing one is found by name instead, which is all
    // the probes look at.
    private static void RegisterHelperClasses()
    {
        RegisterClass("Breadcrumb Parent");
        RegisterClass("ComboBoxEx32");
        // Both usually come with comctl32; where they do, RegisterClassEx fails and CreateWindow finds the
        // real ones by name. Where they do not (a bare test process), a stand-in of the same name is enough
        // for the class-name and control-id probes these tests exercise.
        RegisterClass("SysTreeView32");
        RegisterClass("DUIViewWndClassName");
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

    private static IntPtr CreateChild(string className, IntPtr parent, int controlId)
    {
        var child = CreateWindow(className, parent, WS_CHILD, controlId);
        Assert.AreNotEqual(IntPtr.Zero, child, $"Could not create {className}.");
        return child;
    }

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

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, string text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool enabled);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
}

// Subclasses a dialog's field so the posted Enter can be seen. A real Edit keeps its own behaviour: every
// message goes on to the proc it replaced, and this only notes the one key press the assertions are about.
internal sealed class EnterRecorder : IDisposable
{
    private readonly IntPtr _edit;
    private readonly WndProc _proc;
    private readonly IntPtr _original;
    private volatile bool _seen;

    public EnterRecorder(IntPtr edit)
    {
        _edit = edit;
        _proc = Hook;
        _original = SetWindowLongPtr(edit, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_proc));
        Assert.AreNotEqual(IntPtr.Zero, _original, "could not subclass the field, so the Enter could not be observed");
    }

    // The Enter is posted, not sent, so it only arrives while this thread pumps -- which is exactly what a
    // dialog's own message loop does with it in production.
    public bool ReadQueuedEnter()
    {
        while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        return _seen;
    }

    private IntPtr Hook(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_KEYDOWN && (int)wParam == VK_RETURN) _seen = true;
        return CallWindowProc(_original, hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_original != IntPtr.Zero) SetWindowLongPtr(_edit, GWLP_WNDPROC, _original);
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int GWLP_WNDPROC = -4;
    private const uint PM_REMOVE = 0x0001;
    private const uint WM_KEYDOWN = 0x0100;
    private const int VK_RETURN = 0x0D;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public MSGPOINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSGPOINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);
}
