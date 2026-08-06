using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Lertaro.Core.Services.Pipe;

// Identifies the process/session on the other end of a connected NamedPipeServerStream straight from the
// kernel handle, so privileged handlers can verify who's actually asking instead of trusting anything the
// client claims in the request payload.
internal static class PipeClientIdentity
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientSessionId(SafeHandle pipe, out uint clientSessionId);

    public static bool TryGetClientProcessId(NamedPipeServerStream pipe, out int pid)
    {
        if (GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var raw))
        {
            pid = (int)raw;
            return true;
        }
        pid = 0;
        return false;
    }

    public static bool TryGetClientSessionId(NamedPipeServerStream pipe, out int sessionId)
    {
        if (GetNamedPipeClientSessionId(pipe.SafePipeHandle, out var raw))
        {
            sessionId = (int)raw;
            return true;
        }
        sessionId = 0;
        return false;
    }
}
