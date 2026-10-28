using System.Runtime.InteropServices;
using System.Windows.Threading;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.CoreExtensions.Preview.Handlers;

/// <summary>
/// The one long-lived STA apartment that owns every native preview handler in this process.
///
/// Preview handlers used to be driven from the WPF UI thread, and a handler that blocks -- asking which encoding to
/// import a file as (#459), or merely cold-starting Office -- blocked that thread for seconds while pumping its own
/// messages re-entrantly, so our Hide-on-departure paths ran inside the call and destroyed the host the call was
/// running on. Posting the work instead keeps the UI thread answering the user, keeps teardown in order behind the
/// render it follows, and gives the handler an apartment of its own, which is what a window-creating COM server
/// wants anyway (see project rule: one apartment per concern, never one per call).
/// </summary>
internal static class PreviewStaWorker
{
    // Only ever waited once, on the first preview of the session.
    private const int StartupTimeoutMs = 5000;

    private static readonly object _lock = new();
    private static Dispatcher? _dispatcher;
    private static bool _startFailed;

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    // ponytail: one apartment means one handler call at a time, and a post has no timeout -- a handler wedged inside
    // Initialize or DoPreview queues every later render behind it until it returns. Same accepted ceiling as
    // ShellMenuStaWorker; the upgrade is a bounded wait with a cancellation-aware loop, or one apartment per handler
    // class, not a thread per call.
    internal static void Post(Action work)
    {
        var dispatcher = EnsureStarted();
        if (dispatcher is null)
        {
            // Fall back to running it where we stand rather than dropping the preview: late and possibly freezing
            // beats a preview that never appears. Logged because it means this process has no preview apartment.
            Logger.Log($"[PreviewStaWorker] no preview apartment available, running inline on thread {Environment.CurrentManagedThreadId}", LogLevel.Warn);
            work();
            return;
        }

        // Contained here rather than left to the dispatcher loop: an unhandled exception would take the apartment's
        // thread down, and every later preview would then fall back to the inline path forever.
        dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                Logger.Log($"[PreviewStaWorker] posted work threw: {ex.GetType().Name}: {ex.Message}", LogLevel.Warn);
            }
        }));
    }

    private static Dispatcher? EnsureStarted()
    {
        lock (_lock)
        {
            if (_dispatcher is not null) return _dispatcher;
            if (_startFailed) return null;

            var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                // Handlers create real windows and touch OLE/clipboard; an STA that OLE was never initialized in
                // loads some of them incompletely (the same finding ShellMenuStaWorker records).
                if (OleInitialize(IntPtr.Zero) >= 0)
                {
                    _dispatcher = Dispatcher.CurrentDispatcher;
                    ready.Set();
                    Dispatcher.Run();
                }
                else
                {
                    ready.Set();
                }
            })
            {
                IsBackground = true,
                Name = "PreviewStaWorker",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (!ready.Wait(StartupTimeoutMs) || _dispatcher is null)
            {
                // Sticky: a machine that cannot start this thread will fail the same way every time. The thread, if
                // it is still alive, is a harmless background one and must not be killed mid-COM-initialization.
                _startFailed = true;
                Logger.Log("[PreviewStaWorker] the preview STA thread did not start; preview handlers fall back to the calling thread.", LogLevel.Error);
                return null;
            }

            return _dispatcher;
        }
    }
}
