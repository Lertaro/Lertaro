using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Lertaro.Core.Hook;

// Shared bounded STA dispatcher for plugin adapter/collector reads (IFileDialogAdapter,
// IInlineSearchAdapter, IActivePathCollector). Third-party plugin code can hang inside
// cross-process COM calls, so every such read from tracker machinery runs on a worker apartment with a
// timeout instead of directly on the calling thread.
// Split out of ExplorerActivePathPoller so ExplorerWindowClassifier can share the exact same semantics;
// the abandoned-read budget lives here for both callers.
//
// The workers are long-lived, OLE-initialized and pump messages -- the same three things App's ShellThread
// insists on, for the same reason. A throwaway thread per read is not a bounded read: the apartment dies
// while the target process still holds registrations in it, and an abandoned thread sits in a wait that
// pumps nothing, so the target's own callback into us has nobody to answer it. When the target is
// explorer.exe that is how the shell stops responding for the whole desktop -- reported on Windows 10 22H2
// as "open folder fails, Win+E does nothing, and every queued window appears at once when Lertaro exits",
// which is exactly what tearing this process down would release.
internal static class ExplorerStaInvoker
{
    // Four, so one read parked inside a wedged shell call cannot hold the next one behind it.
    // ponytail: fixed at startup and never grown, because growing it means asking the loader for a thread
    // from wherever work arrives -- the thing that wedged a shell menu's extension elsewhere in this app.
    private const int WorkerCount = 4;
    private const int WorkerStartupTimeoutMs = 5000;

    // Reads whose worker has not answered within their timeout. While at the cap, new reads fail fast to
    // their fallback, so a hung shell extension cannot queue unbounded work behind the wedge.
    private const int MaxAbandonedReads = 8;
    private static int _abandonedReads;

    private static readonly List<Dispatcher> Workers = new();
    private static readonly object Gate = new();
    private static bool _started;
    private static int _next;

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    /// <summary>
    /// Creates the workers. Called when the tracker starts, so the first foreground change of the session
    /// never asks the loader for four threads from the thread that exists to be dispatched to.
    /// </summary>
    public static void Start() => EnsureWorkers();

    public static T RunOnStaWithTimeout<T>(Func<T> func, T fallback, TimeSpan timeout)
        => RunOnStaWithTimeout(func, fallback, timeout, out _);

    public static T RunOnStaWithTimeout<T>(Func<T> func, T fallback, TimeSpan timeout, out bool timedOut)
    {
        timedOut = false;
        if (Volatile.Read(ref _abandonedReads) >= MaxAbandonedReads)
        {
            Logger.Log("[ExplorerStaInvoker] Abandoned-read budget exhausted; failing the read fast.", LogLevel.Warn);
            timedOut = true;
            return fallback;
        }

        var worker = NextWorker();
        if (worker == null)
        {
            Logger.Log("[ExplorerStaInvoker] No STA worker available; failing the read fast.", LogLevel.Warn);
            timedOut = true;
            return fallback;
        }

        var result = fallback;
        Exception? error = null;
        // The callback swallows because it runs on a shared, long-lived dispatcher: an exception escaping
        // here would surface on that dispatcher's unhandled-exception path and take the process with it.
        var operation = worker.InvokeAsync(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
            return true;
        });

        // Blocking the caller is fine -- it is an event thread, and the apartment that has to answer the
        // target process is the worker's own, which pumps whether it is idle or inside this call.
        if (operation.Task.Wait(timeout))
        {
            if (error != null)
                Logger.Log($"[ExplorerStaInvoker] Plugin read failed: {error.Message}", LogLevel.Warn);
            return result;
        }

        timedOut = true;
        Interlocked.Increment(ref _abandonedReads);
        // The read still owns that worker until it comes back; the budget is what stops the next hundred
        // from piling up behind it.
        operation.Task.ContinueWith(_ => Interlocked.Decrement(ref _abandonedReads), CancellationToken.None,
            TaskContinuationOptions.None, TaskScheduler.Default);
        Logger.Log("[ExplorerStaInvoker] Plugin read timed out; continuing with fallback.", LogLevel.Warn);
        return fallback;
    }

    private static void EnsureWorkers()
    {
        lock (Gate)
        {
            if (_started) return;
            _started = true;
            for (var index = 0; index < WorkerCount; index++)
                if (CreateWorker(index) is { } worker)
                    Workers.Add(worker);
        }
    }

    private static Dispatcher? NextWorker()
    {
        EnsureWorkers();
        lock (Gate)
        {
            if (Workers.Count == 0) return null;
            _next = (_next + 1) % Workers.Count;
            return Workers[_next];
        }
    }

    private static Dispatcher? CreateWorker(int index)
    {
        using var ready = new ManualResetEventSlim(false);
        Dispatcher? created = null;
        var thread = new Thread(() =>
        {
            // Folder handlers, data objects and the shell objects a read hands back expect an
            // OLE-initialized apartment; without it they load incompletely.
            OleInitialize(IntPtr.Zero);
            created = Dispatcher.CurrentDispatcher;
            ready.Set();
            // A real message pump, so COM calls coming back into an object this apartment created are
            // serviced -- while the worker idles and, inside a blocking cross-process call, by COM itself.
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = $"ExplorerStaWorker{index}",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!ready.Wait(TimeSpan.FromMilliseconds(WorkerStartupTimeoutMs)))
        {
            // Falling back to the calling thread's dispatcher would put the read on an event thread that
            // exists to be dispatched TO, not to do the reading: that is the wedge this class is for.
            Logger.Log($"[ExplorerStaInvoker] Worker {index} failed to start within {WorkerStartupTimeoutMs}ms", LogLevel.Error);
            return null;
        }

        return created;
    }
}
