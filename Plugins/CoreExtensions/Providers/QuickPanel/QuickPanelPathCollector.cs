using System.Runtime.InteropServices;
using System.Text;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.QuickPanel;

/// <summary>
/// Treats the quick panel as a file manager while it is foreground: the panel answers a documented
/// WM_COPYDATA request with the selected existing file's parent directory, so no state survives its window.
/// </summary>
public sealed class QuickPanelPathCollector : IActivePathCollector
{
    private const uint WM_COPYDATA = 0x004A;
    private const uint MSGFLT_ALLOW = 1;
    // Must match QuickPanelWindowPathRequests: this is the private protocol between the core plugin and App.
    private const int RequestParentDirectory = 0x534C5150;
    private const int ReplyParentDirectory = 0x534C5151;
    private const string QuickPanelWindowTitle = "Lertaro Quick Panel";
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private const string QueryClassName = "LertaroQuickPanelPathQuery";
    private static readonly object ClassLock = new();
    private static bool _classRegistered;
    private static WndProcDelegate? _wndProc;

    [ThreadStatic] private static string? _capturedPath;

    public string Name => TranslationService.Get("Plugins_QuickPanelTargetName");
    public string TargetName => TranslationService.Get("Plugins_QuickPanelTargetName");

    // The WPF wrapper class is shared by Lertaro windows, so the concrete-window overload below is
    // required to identify the Quick Panel by its title without claiming the other Lertaro windows.
    public bool CanHandle(string className) => false;

    public bool CanHandle(string windowClassName, string windowTitle) =>
        IsQuickPanelWindow(windowClassName, windowTitle);

    public bool CanHandle(IntPtr windowHwnd, string windowClassName, string processName) =>
        IsQuickPanelWindow(windowClassName, GetWindowTitle(windowHwnd));

    internal static bool IsQuickPanelWindow(string windowClassName, string windowTitle) =>
        windowClassName.StartsWith("HwndWrapper[", StringComparison.Ordinal) &&
        windowTitle.Equals(QuickPanelWindowTitle, StringComparison.Ordinal);

    public string? TryGetPath(IntPtr activeHwnd, string activeClassName, IntPtr windowHwnd, string windowClassName, string processName)
    {
        var panelHwnd = windowHwnd != IntPtr.Zero ? windowHwnd : activeHwnd;
        return QueryParentDirectory(panelHwnd);
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length <= 0) return string.Empty;

        var text = new StringBuilder(length + 1);
        return GetWindowText(hWnd, text, text.Capacity) == 0 ? string.Empty : text.ToString();
    }

    private static string? QueryParentDirectory(IntPtr panelHwnd)
    {
        if (panelHwnd == IntPtr.Zero) return null;
        EnsureClassRegistered();

        var receiver = CreateWindowExW(0, QueryClassName, null, 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (receiver == IntPtr.Zero) return null;

        ChangeWindowMessageFilterEx(receiver, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
        try
        {
            _capturedPath = null;
            var request = new CopyDataStruct { DataType = (IntPtr)RequestParentDirectory };
            SendMessage(panelHwnd, WM_COPYDATA, receiver, ref request);
            return string.IsNullOrEmpty(_capturedPath) ? null : _capturedPath;
        }
        finally
        {
            DestroyWindow(receiver);
        }
    }

    private static void EnsureClassRegistered()
    {
        lock (ClassLock)
        {
            if (_classRegistered) return;

            _wndProc = QueryWindowProc;
            var windowClass = new WindowClass
            {
                WindowProc = _wndProc,
                Instance = GetModuleHandle(null),
                ClassName = QueryClassName
            };
            RegisterClassW(ref windowClass);
            _classRegistered = true;
        }
    }

    private static IntPtr QueryWindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WM_COPYDATA)
        {
            var data = Marshal.PtrToStructure<CopyDataStruct>(lParam);
            if (data.DataType == (IntPtr)ReplyParentDirectory && data.Data != IntPtr.Zero && data.ByteCount > 0)
                _capturedPath = Marshal.PtrToStringUni(data.Data, data.ByteCount / sizeof(char))?.TrimEnd('\0');
            return (IntPtr)1;
        }

        return DefWindowProcW(hWnd, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStruct
    {
        public IntPtr DataType;
        public int ByteCount;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public WndProcDelegate WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        [MarshalAs(UnmanagedType.LPWStr)] public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, ref CopyDataStruct data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint extendedStyle, string className, string? windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, IntPtr changeInfo);
}
