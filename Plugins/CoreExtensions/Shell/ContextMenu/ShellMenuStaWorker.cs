using System.Runtime.InteropServices;
using System.Windows.Threading;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.CoreExtensions.Shell.ContextMenu;

// Shell context-menu COM objects must live in a stable STA. Host them on dedicated, long-lived STA
// threads so the menu can be built OFF the UI thread (a slow shell extension no longer freezes the
// whole actions list) while the COM objects stay valid for a later InvokeCommand.
//
// A buggy third-party handler (e.g. a cloud-drive context menu) can block inside QueryContextMenu
// while holding the Windows loader lock. Every thread started AFTER that point then hangs in
// LdrpInitializeThread: the process stops being able to create threads -- message pump included --
// and only a force-kill helps. That is why the workers are a fixed pool, started together BEFORE
// the first shell extension is loaded, and never grown later: acquiring a worker must never mean
// asking the loader for a new thread.
internal static class ShellMenuStaWorker
{
    // Bounds how long a caller waits on one STA worker. A shell extension stuck in a native call can
    // wedge that thread forever; without this, every caller -- including the actions-menu build --
    // would block indefinitely right along with it.
    public const int InvokeTimeoutMs = 5000;

    // One wedged extension pins the apartment it ran on, but a fresh menu open still needs one. Four
    // covers a menu that opens while the previous one is still being torn down; when they are gone the
    // native menu is skipped for the rest of the process rather than starting a thread under a wedge.
    internal const int WorkerCount = 4;

    private const int NotStarted = 0;
    private const int Starting = 1;
    private const int Started = 2;

    private static readonly object _lock = new();
    private static readonly Queue<Dispatcher> _spare = new();
    private static readonly ManualResetEventSlim _poolReady = new();
    private static int _startState;
    // Sticky: a machine where no worker can start (loader lock already held, DCOM policy, security
    // software) will fail the exact same way every time. Retrying per menu open would just leak more
    // threads wedged in startup; give up once, permanently, for this process.
    private static volatile bool _startFailed;

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    // Takes one idle apartment out of the pool, or null when none is available. Starts the whole pool
    // on the first call -- before any third-party shell code has run -- and never starts a thread again.
    public static Dispatcher? Acquire()
    {
        if (_startFailed) return null;

        if (Volatile.Read(ref _startState) == NotStarted
            && Interlocked.CompareExchange(ref _startState, Starting, NotStarted) == NotStarted)
        {
            // Not under _lock: if thread startup itself hangs on this machine, the lock must stay free
            // so a session's Dispose can still return instead of freezing its caller forever.
            try
            {
                StartWorkers();
            }
            catch (Exception ex)
            {
                Logger.Log($"[ShellMenuSession] Starting the STA worker pool failed: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                lock (_lock)
                {
                    if (_spare.Count == 0)
                    {
                        _startFailed = true;
                        Logger.Log("[ShellMenuSession] No STA worker could be started; disabling the native shell context menu for this process.", LogLevel.Error);
                    }
                    Volatile.Write(ref _startState, Started);
                }
                _poolReady.Set();
            }
        }
        else if (!_poolReady.Wait(InvokeTimeoutMs))
        {
            // Startup is stuck, most likely on the loader lock this pool exists to avoid. Give up once
            // instead of making every later menu open wait on the same dead end.
            _startFailed = true;
            Logger.Log("[ShellMenuSession] STA pool startup did not finish in time; disabling the native shell context menu for this process.", LogLevel.Error);
            return null;
        }

        lock (_lock)
        {
            if (_startFailed) return null;
            if (_spare.Count == 0)
            {
                Logger.Log($"[ShellMenuSession] All {WorkerCount} STA workers are held by hung shell extensions; skipping the native shell context menu.", LogLevel.Warn);
                return null;
            }

            return _spare.Dequeue();
        }
    }

    // Returns a worker whose apartment is still healthy. Wedged workers are simply never returned, so
    // they cannot be handed to a later session.
    public static void Release(Dispatcher worker)
    {
        if (_startFailed) return;

        lock (_lock)
        {
            _spare.Enqueue(worker);
        }
    }

    private static void StartWorkers()
    {
        var ready = new ManualResetEventSlim[WorkerCount];
        var dispatchers = new Dispatcher?[WorkerCount];

        // Start them all before waiting on any: a thread that cannot start would otherwise eat the
        // whole timeout budget on its own, four times over.
        for (var i = 0; i < WorkerCount; i++)
        {
            var index = i;
            var started = new ManualResetEventSlim();
            ready[index] = started;
            var thread = new Thread(() =>
            {
                // Shell context-menu handlers (especially folder ones -- drag-drop, data objects,
                // cloud/overlay providers) require an OLE-initialized STA. The WPF UI thread has this;
                // a bare worker thread does not, so initialize OLE here or those handlers load
                // incompletely (missing/changing items on first open).
                if (OleInitialize(IntPtr.Zero) >= 0)
                {
                    dispatchers[index] = Dispatcher.CurrentDispatcher;
                    started.Set();
                    Dispatcher.Run();
                }
                else
                {
                    // Signal with a null dispatcher: the starter reports this worker as unavailable
                    // instead of waiting out the timeout.
                    started.Set();
                }
            })
            {
                IsBackground = true,
                Name = $"ShellMenuStaWorker{i}"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        for (var i = 0; i < WorkerCount; i++)
        {
            if (ready[i].Wait(InvokeTimeoutMs) && dispatchers[i] != null)
            {
                _spare.Enqueue(dispatchers[i]!);
            }
            else
            {
                // The thread (if it is still alive) stays a harmless background thread: IsBackground
                // keeps it from blocking process exit, and killing it mid-startup risks corrupting COM
                // state for the rest of the process.
                Logger.Log("[ShellMenuSession] An STA worker failed to start in time; one fewer native shell menu slot.", LogLevel.Warn);
            }

            // Deliberately not disposed: a worker that finishes initialising after this timeout still
            // calls started.Set(), and disposing underneath it would throw on that thread.
        }
    }
}
