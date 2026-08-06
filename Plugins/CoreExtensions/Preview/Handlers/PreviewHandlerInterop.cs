using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Lertaro.Plugins.CoreExtensions.Preview.Handlers;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public IntPtr Hwnd;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public int PtX;
    public int PtY;
}

// Win32/COM surface for hosting a Windows Preview Handler (IPreviewHandler). No external dependencies.
internal static class PreviewHandlerInterop
{
    // {8895b1c6-b41f-4c1c-a562-0d564250836f} is both the IPreviewHandler IID and the shellex category key.
    public static readonly Guid IID_IPreviewHandler = new("8895b1c6-b41f-4c1c-a562-0d564250836f");
    public static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    [Flags]
    public enum CLSCTX : uint
    {
        INPROC_SERVER = 0x1,
        LOCAL_SERVER = 0x4,
        NO_CODE_DOWNLOAD = 0x400,
    }

    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;
    public const uint STGM_READ = 0x0;
    public const uint STGM_SHARE_DENY_WRITE = 0x20;
    public const uint GW_CHILD = 5;

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(in Guid clsid, IntPtr pUnkOuter, CLSCTX dwClsContext, in Guid iid, out IntPtr ppv);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int SHCreateStreamOnFileEx(string pszFile, uint grfMode, uint dwAttributes, bool fCreate, IntPtr pstmTemplate, out ComTypes.IStream ppstm);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, in Guid riid, out IShellItem ppv);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(int exStyle, string className, string? windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hwnd, uint uCmd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}

[ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPreviewHandler
{
    void SetWindow(IntPtr hwnd, in RECT rect);
    void SetRect(in RECT rect);
    void DoPreview();
    void Unload();
    void SetFocus();
    void QueryFocus(out IntPtr phwnd);
    [PreserveSig] int TranslateAccelerator(ref MSG pmsg);
}

[ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInitializeWithFile
{
    void Initialize([MarshalAs(UnmanagedType.LPWStr)] string pszFilePath, uint grfMode);
}

[ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInitializeWithStream
{
    void Initialize(ComTypes.IStream pstream, uint grfMode);
}

[ComImport, Guid("7f73be3f-fb79-493c-a6c7-7ee14e245841"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInitializeWithItem
{
    void Initialize(IShellItem psi, uint grfMode);
}

[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(IntPtr pbc, in Guid bhid, in Guid riid, out IntPtr ppv);
    void GetParent(out IShellItem ppsi);
    void GetDisplayName(uint sigdnName, out IntPtr ppszName);
    void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    void Compare(IShellItem psi, uint hint, out int piOrder);
}
