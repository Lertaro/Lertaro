using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Lertaro.PluginSdk.Shell.FileOperations;

// Shared dedicated STA thread for IFileOperation-based work (delete, paste/copy/move -- anything
// whose native confirm/progress/conflict dialog can legitimately sit open for a while waiting on the
// user). Kept separate from ShellMenuSession's own STA worker (context-menu enumeration), so a slow
// file operation's dialog can't stall quick right-click lookups, and vice versa.
internal static class ShellOperationStaWorker
{
    private static Dispatcher? _staDispatcher;
    private static readonly object _staLock = new();

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    public static Dispatcher? StaDispatcher
    {
        get
        {
            if (_staDispatcher != null) return _staDispatcher;
            lock (_staLock)
            {
                if (_staDispatcher != null) return _staDispatcher;
                using var ready = new ManualResetEventSlim();
                var thread = new Thread(() =>
                {
                    OleInitialize(IntPtr.Zero);
                    _staDispatcher = Dispatcher.CurrentDispatcher;
                    ready.Set();
                    Dispatcher.Run();
                })
                {
                    IsBackground = true,
                    Name = "ShellFileOperationStaWorker"
                };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();

                if (!ready.Wait(TimeSpan.FromSeconds(5)))
                {
                    Logger.Log("[ShellOperationStaWorker] STA worker failed to start within 5s.", LogLevel.Error);
                    return null;
                }

                return _staDispatcher;
            }
        }
    }
}
