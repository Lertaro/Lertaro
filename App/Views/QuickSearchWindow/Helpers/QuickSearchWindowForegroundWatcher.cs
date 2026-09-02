using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Lertaro.Core;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

public class QuickSearchWindowForegroundWatcher
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0;

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
    private WinEventDelegate? _hookDelegate;
    private IntPtr _hHook = IntPtr.Zero;

    private readonly Lertaro.App.QuickSearchWindow _window;
    private readonly Action _hideWindow;

    public QuickSearchWindowForegroundWatcher(Lertaro.App.QuickSearchWindow window, Action hideWindow)
    {
        _window = window;
        _hideWindow = hideWindow;
    }

    public void Start()
    {
        if (_hHook != IntPtr.Zero) return;
        _hookDelegate = ForegroundEventProc;
        _hHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _hookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public void Stop()
    {
        if (_hHook != IntPtr.Zero)
        {
            UnhookWinEvent(_hHook);
            _hHook = IntPtr.Zero;
        }
        _hookDelegate = null;
    }

    private void ForegroundEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var sbClass = new StringBuilder(256);
            GetClassName(hwnd, sbClass, sbClass.Capacity);
            var className = sbClass.ToString();
            GetWindowThreadProcessId(hwnd, out var activePid);

            // A preview provider may be hosting an out-of-process native handler (e.g. Office acting as
            // its own Preview Handler COM server), whose window can grab foreground on its own -- at
            // startup or from interacting with its content (e.g. a right-click menu) -- for as long as
            // it's shown. See PreviewActivationSignal. That isn't the user switching to another app.
            if (PluginSdk.Services.PreviewActivationSignal.IsActive) return;

            if (className.Contains("InputSwitch", StringComparison.OrdinalIgnoreCase)) return;

            if (activePid == (uint)Environment.ProcessId) return;

            // A transient foreground steal can happen mid-typing without the user actually switching away
            // -- e.g. rendering a \\wsl$ result's icon/modified date wakes the WSL VM, whose cold start
            // briefly flashes a console host that grabs foreground (see the identical wait-and-recheck in
            // QuickSearchWindow.Window_Deactivated, added for the same reason). That path alone isn't
            // enough: this hook fires independently and used to hide immediately on the very first event.
            // Debounce here too, so foreground that bounces right back doesn't drop the search window.
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_window.IsVisible) return;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                timer.Tick += (s, _) =>
                {
                    timer.Stop();
                    if (!_window.IsVisible) return;
                    GetWindowThreadProcessId(GetForegroundWindow(), out var stillActivePid);
                    if (stillActivePid == (uint)Environment.ProcessId) return;
                    _hideWindow();
                };
                timer.Start();
            }), DispatcherPriority.Background);
        }
        catch { }
    }
}
