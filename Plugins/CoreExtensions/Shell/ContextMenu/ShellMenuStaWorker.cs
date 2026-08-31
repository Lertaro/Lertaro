using System.Runtime.InteropServices;
using System.Windows.Threading;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.CoreExtensions.Shell.ContextMenu;

// Shell context-menu COM objects must live in a stable STA. Host them on a dedicated, long-lived
// STA thread so the menu can be built OFF the UI thread (a slow shell extension no longer freezes
// the whole actions list) while the COM objects stay valid for a later InvokeCommand.
internal static class ShellMenuStaWorker
{
    // Bounds how long a caller waits on this shared STA worker. A misbehaving shell extension can
    // wedge that thread on a native call forever; without this, every caller -- including the startup
    // warm-up's background task -- would block indefinitely right along with it.
    public const int InvokeTimeoutMs = 5000;

    private static Dispatcher? _staDispatcher;
    private static uint _staThreadId;
    private static readonly object _staLock = new();
    // Sticky: a machine where the STA worker never finishes starting (broken/hooked shell extension,
    // DCOM policy, security software) will fail the exact same way every time. Retrying per-call would
    // just spawn another thread wedged forever in OleInitialize on top of the last one -- give up once,
    // permanently, for this process.
    private static bool _staInitFailed;

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    public static Dispatcher? StaDispatcher
    {
        get
        {
            if (_staInitFailed) return null;
            if (_staDispatcher != null) return _staDispatcher;
            lock (_staLock)
            {
                if (_staInitFailed) return null;
                if (_staDispatcher != null) return _staDispatcher;
                using var ready = new ManualResetEventSlim();
                var thread = new Thread(() =>
                {
                    // Shell context-menu handlers (especially folder ones — drag-drop, data objects,
                    // cloud/overlay providers) require an OLE-initialized STA. The WPF UI thread has
                    // this; a bare worker thread does not, so initialize OLE here or those handlers load
                    // incompletely (missing/changing items on first open).
                    OleInitialize(IntPtr.Zero);
                    _staThreadId = GetCurrentThreadId();
                    _staDispatcher = Dispatcher.CurrentDispatcher;
                    ready.Set();
                    Dispatcher.Run();
                })
                {
                    IsBackground = true,
                    Name = "ShellMenuStaWorker"
                };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();

                // If OleInitialize/thread startup itself never completes on this machine, this used to
                // wait forever while holding _staLock -- wedging every other caller (including the
                // startup warm-up) on this same lock right along with it. The thread we just leaked stays
                // a harmless background thread (IsBackground=true won't block process exit); forcibly
                // killing it mid-OleInitialize risks corrupting COM state for the rest of the process, so
                // it's simply abandoned.
                if (!ready.Wait(InvokeTimeoutMs))
                {
                    Logger.Log($"[ShellMenuSession] STA worker failed to start within {InvokeTimeoutMs}ms; disabling the native shell context menu for this session.", LogLevel.Error);
                    _staInitFailed = true;
                    return null;
                }

                return _staDispatcher!;
            }
        }
    }

    // A shell extension stuck in a native call can wedge this thread forever. TerminateThread is not a
    // safe way out (no stack unwind, no COM release, and it can leave process-wide locks held), so the
    // wedged background STA thread is simply abandoned and a fresh worker is created for the next call.
    public static void KillWedgedStaWorker(Dispatcher wedgedDispatcher)
    {
        lock (_staLock)
        {
            if (_staDispatcher != wedgedDispatcher) return; // already replaced by another caller's timeout

            _staDispatcher = null;
            _staThreadId = 0;
            Logger.Log("[ShellMenuSession] Abandoned wedged STA worker; a fresh worker will start on the next call.", LogLevel.Warn);
        }
    }
}
