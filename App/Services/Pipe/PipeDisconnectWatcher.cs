using System.IO.Pipes;
using System.Runtime.InteropServices;
using Lertaro.Core;

namespace Lertaro.App.Services.Pipe;

internal static class PipeDisconnectWatcher
{
    private const int PollIntervalMs = 25;

    public static async Task WatchAsync(NamedPipeServerStream pipe, CancellationTokenSource queryCts, CancellationToken stopToken)
    {
        var handle = pipe.SafePipeHandle;
        try
        {
            while (!stopToken.IsCancellationRequested)
            {
                await Task.Delay(PollIntervalMs, stopToken).ConfigureAwait(false);
                if (handle.IsClosed || handle.IsInvalid)
                    return;

                if (!Win32Api.PeekNamedPipe(handle, IntPtr.Zero, 0, IntPtr.Zero, out _, IntPtr.Zero) &&
                    Marshal.GetLastWin32Error() == Win32Api.ERROR_BROKEN_PIPE)
                {
                    queryCts.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
