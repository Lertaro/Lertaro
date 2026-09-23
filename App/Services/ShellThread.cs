using System.Runtime.InteropServices;
using System.Windows.Threading;
using Lertaro.Core;

namespace Lertaro.App.Services;

/// <summary>
/// Runs shell work on a small pool of dedicated single-threaded-apartment threads, off whatever thread
/// asked for it.
/// </summary>
/// <remarks>
/// Every shell entry point this app uses can block without a deadline. Directory.Exists and File.Exists
/// wait out the SMB timeout on a mapped drive whose server is gone; SHParseDisplayName contacts the
/// server behind a UNC path; SHOpenFolderAndSelectItems routes through Explorer, which does not return
/// while Explorer is busy; and ShellExecuteEx (what Process.Start with UseShellExecute does) hands off to
/// whatever shell extension is registered for the target. Called inline from a key press or a menu click,
/// any of those takes the window down with no way back but Task Manager.
///
/// STA specifically, rather than the thread pool: ShellExecuteEx delegates to shell extensions that use
/// COM, and some of them require a single-threaded apartment. The pool is MTA, so work scheduled there is
/// running against that documented requirement even when it happens to work.
///
/// The threads are created once and work is posted to them, rather than one thread started per action.
/// Thread.Start does not return until the new thread has finished its loader initialisation, so starting
/// one on the UI thread puts the UI thread in line behind whatever the loader is doing -- and this process
/// hosts third-party shell extensions (see CoreExtensions' ShellMenuSession), one of which was observed
/// holding that up indefinitely. A click on "open containing folder" then froze the window as "not
/// responding" without ever reaching the shell call. Building the pool at startup, before any shell menu
/// has put a foreign extension in the process, means no user action ever asks the loader for a thread.
/// </remarks>
internal static class ShellThread
{
    // Four, so that one wedged SHOpenFolderAndSelectItems (it waits on Explorer, which can be busy) cannot
    // stall the click-driven quick-navigation gate waiting behind it.
    // ponytail: fixed at startup and never grown, because growing it means starting a thread from wherever
    // work arrives -- the exact thing the class comment rules out. If a fifth concurrently hung shell
    // action ever becomes the problem, the upgrade is to pre-start more workers, not to start them on
    // demand.
    private const int WorkerCount = 4;
    private const int WorkerStartupTimeoutMs = 5000;

    private static readonly List<Dispatcher> Workers = new();
    private static readonly object Gate = new();
    private static bool _started;
    private static int _next;

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    /// <summary>
    /// Creates the workers. Called from app startup so that the first shell action of the session never
    /// pays for it; posting work before this is still safe, since <see cref="Run"/> creates them itself if
    /// nobody has asked yet.
    /// </summary>
    public static void Start() => EnsureWorkers();

    public static void Run(string name, Action action)
    {
        EnsureWorkers();

        Dispatcher worker;
        lock (Gate)
        {
            if (Workers.Count == 0)
            {
                // No worker could be created at all, which on a machine this healthy means the loader is
                // already wedged -- running the action inline would hang the caller, and starting a thread
                // of one's own is what this class exists not to do.
                Logger.Log($"[ShellThread] No shell worker available; dropped '{name}'", LogLevel.Error);
                return;
            }

            _next = (_next + 1) % Workers.Count;
            worker = Workers[_next];
        }

        worker.InvokeAsync(() =>
        {
            // An exception escaping here surfaces on a shared, long-lived dispatcher's unhandled-exception
            // path instead of dying with a throwaway thread, and would take the process with it.
            try { action(); }
            catch (Exception ex) { Logger.Log($"[ShellThread] '{name}' threw: {ex.Message}", LogLevel.Error); }
        });
    }

    private static void EnsureWorkers()
    {
        lock (Gate)
        {
            if (_started)
                return;
            _started = true;

            for (var index = 0; index < WorkerCount; index++)
            {
                var dispatcher = CreateWorker(index);
                if (dispatcher != null)
                    Workers.Add(dispatcher);
            }
        }
    }

    private static Dispatcher? CreateWorker(int index)
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? created = null;
        var thread = new Thread(() =>
        {
            // The same OLE initialisation the other two long-lived shell apartments do this for
            // (ShellMenuStaWorker, ShellOperationStaWorker): folder handlers, data objects and overlay
            // providers a shell call hands back expect an OLE-initialized apartment, and without it they
            // load incompletely.
            OleInitialize(IntPtr.Zero);
            created = Dispatcher.CurrentDispatcher;
            ready.Set();
            // A real message pump rather than a blocking queue wait, so that COM calls coming back into an
            // object this apartment created are serviced while the worker sits idle.
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = $"ShellThread{index}",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!ready.Wait(TimeSpan.FromMilliseconds(WorkerStartupTimeoutMs)))
        {
            Logger.Log($"[ShellThread] Worker {index} failed to start within {WorkerStartupTimeoutMs}ms", LogLevel.Error);
            return null;
        }

        return created;
    }
}
