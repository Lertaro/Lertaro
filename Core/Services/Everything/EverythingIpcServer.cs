using System.Runtime.InteropServices;

namespace Lertaro.Core.Services.Everything;

/// <summary>Hosts the Win32 EVERYTHING_TASKBAR_NOTIFICATION hidden window for Everything IPC emulation.</summary>
public sealed class EverythingIpcServer : IDisposable
{
    private readonly EverythingIpcMessageDispatcher _dispatcher;
    private readonly object _lock = new();
    private Thread? _messageThread;
    private IntPtr _hwnd;
    private bool _isRunning;
    private int _disposed;
    private WndProcDelegate? _wndProc;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr pChangeFilterStruct);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    private const uint MSGFLT_ALLOW = 1;
    private static readonly IntPtr HWND_BROADCAST = (IntPtr)0xffff;

    public EverythingIpcServer(EverythingIpcMessageDispatcher dispatcher) => _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public EverythingIpcServer(IEverythingDataProvider dataProvider)
        : this(new EverythingIpcMessageDispatcher(dataProvider))
    {
    }

    public bool IsRunning => _isRunning;
    public IntPtr Hwnd => _hwnd;

    public bool Start()
    {
        lock (_lock)
        {
            if (_isRunning || _disposed != 0) return true;

            var startedEvent = new ManualResetEventSlim(false);
            _messageThread = new Thread(() => RunMessageLoop(startedEvent))
            {
                Name = "EverythingIpcServerThread",
                IsBackground = true
            };
            _messageThread.SetApartmentState(ApartmentState.STA);
            _messageThread.Start();

            if (startedEvent.Wait(TimeSpan.FromSeconds(3)))
            {
                _isRunning = _hwnd != IntPtr.Zero;
                return _isRunning;
            }

            return false;
        }
    }

    private void RunMessageLoop(ManualResetEventSlim startedEvent)
    {
        var hInstance = GetModuleHandle(null);
        var className = EverythingIpcConstants.TaskbarNotificationWndClass;
        _wndProc = CustomWndProc;

        var wcex = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = className,
            hIconSm = IntPtr.Zero
        };

        RegisterClassEx(ref wcex);

        _hwnd = CreateWindowEx(
            dwExStyle: 0,
            lpClassName: className,
            lpWindowName: string.Empty,
            dwStyle: 0,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent: IntPtr.Zero,
            hMenu: IntPtr.Zero,
            hInstance: hInstance,
            lpParam: IntPtr.Zero);

        if (_hwnd != IntPtr.Zero)
        {
            // Allow messages across UIPI boundaries (admin vs standard user)
            ChangeWindowMessageFilterEx(_hwnd, EverythingIpcConstants.WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
            ChangeWindowMessageFilterEx(_hwnd, EverythingIpcConstants.WM_USER, MSGFLT_ALLOW, IntPtr.Zero);

            // Broadcast that Everything IPC is created and ready
            var createdMsg = RegisterWindowMessage(EverythingIpcConstants.CreatedBroadcastMessageName);
            if (createdMsg != 0)
            {
                PostMessage(HWND_BROADCAST, createdMsg, IntPtr.Zero, IntPtr.Zero);
            }

            Logger.Log($"[EverythingIpcServer] Registered and listening on window {_hwnd:X}", LogLevel.Debug);
        }
        else
        {
            Logger.Log($"[EverythingIpcServer] Failed to create window: {Marshal.GetLastWin32Error()}", LogLevel.Warn);
        }

        startedEvent.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        UnregisterClass(className, hInstance);
    }

    private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) => msg switch
    {
        EverythingIpcConstants.EverythingWmIpc => _dispatcher.HandleIpcCommand((int)wParam.ToInt64(), lParam),
        EverythingIpcConstants.WM_COPYDATA => _dispatcher.HandleCopyData(wParam, lParam, hWnd),
        _ => DefWindowProc(hWnd, msg, wParam, lParam),
    };

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning) return;
            _isRunning = false;

            if (_hwnd != IntPtr.Zero)
            {
                PostMessage(_hwnd, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
                PostMessage(_hwnd, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
            }

            if (_messageThread != null && _messageThread.IsAlive)
            {
                _messageThread.Join(1500);
                _messageThread = null;
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Stop();
    }
}
