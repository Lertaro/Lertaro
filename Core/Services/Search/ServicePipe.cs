using System.IO.Pipes;
using System.Runtime.InteropServices;
using Lertaro.Core.Hook.Ipc;

namespace Lertaro.Core.Services.Search;

/// <summary>
/// Connects to "LertaroPipe" and makes sure the process answering is the Lertaro service.
/// </summary>
/// <remarks>
/// The pipe's DACL stops anyone else from adding an instance while the service runs, but once the service
/// is stopped -- which any signed-in user may do, so the tray can stop it without a UAC prompt -- nothing
/// stops another user's process from creating the name. Everything this App trusts from the service (search
/// results, the PID of the hook it launched, which the hook pipe check then compares against) would come
/// from that process instead. So the server's PID, which the kernel reports and the server cannot choose,
/// has to be the PID the Service Control Manager has for LertaroService.
///
/// ponytail: one SCM round trip per connection, including each keystroke's search. Measured against the
/// running service at 137 us median, 175 us p95, which is not worth a cache and the invalidation that comes
/// with one. If a machine ever shows it, cache the PID while holding a handle to that process (a PID cannot
/// be reused while a handle to it is open). <see cref="CallerVisibility.ForClient"/> counts the dearer half
/// of what a connection now costs.
/// </remarks>
internal static class ServicePipe
{
    public const string Name = "LertaroPipe";
    private const string ServiceName = "LertaroService";

    // ponytail: a debug build also talks to the service run from a console (Service\Program.cs's fallback),
    // which the SCM knows nothing about. It is accepted only while the SCM reports no running service, so
    // an installed service is still verified in a debug build.
#if DEBUG
    private const bool AllowUnregisteredServer = true;
#else
    private const bool AllowUnregisteredServer = false;
#endif

    /// <summary>
    /// Connected pipe to the genuine service. Throws on a timeout, a cancelled wait or a server that is
    /// not the service; callers already treat a failed connect as the service being unavailable.
    /// </summary>
    public static async Task<NamedPipeClientStream> ConnectAsync(int timeoutMs, CancellationToken token)
    {
        var pipe = new NamedPipeClientStream(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(timeoutMs, token).ConfigureAwait(false);
            var serverPid = HookPipePeer.TryGetServerProcessId(pipe);
            var servicePid = QueryServiceProcessId();
            if (!IsTrustedServer(serverPid, servicePid, AllowUnregisteredServer))
            {
                throw new UnauthorizedAccessException(
                    $"{Name} is served by PID {serverPid?.ToString() ?? "unknown"}, not by {ServiceName} (PID {servicePid}).");
            }

            return pipe;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Whether a server owned by <paramref name="serverPid"/> is the service the SCM runs as
    /// <paramref name="servicePid"/> (0 when it is not running or could not be asked). Fails closed on
    /// anything unknown. <paramref name="allowUnregisteredServer"/> accepts any server, but only while
    /// no service is running (see <see cref="AllowUnregisteredServer"/>).
    /// </summary>
    internal static bool IsTrustedServer(int? serverPid, uint servicePid, bool allowUnregisteredServer) =>
        servicePid == 0 ? allowUnregisteredServer : serverPid == servicePid;

    /// <summary>The PID the SCM has for the running service, or 0 when it is stopped or unknown.</summary>
    private static uint QueryServiceProcessId()
    {
        const uint scManagerConnect = 0x0001, serviceQueryStatus = 0x0004, serviceRunning = 4;
        var manager = OpenSCManagerW(null, null, scManagerConnect);
        if (manager == IntPtr.Zero)
            return 0;
        try
        {
            var service = OpenServiceW(manager, ServiceName, serviceQueryStatus);
            if (service == IntPtr.Zero)
                return 0;
            try
            {
                return QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceStatusProcess>(), out _) &&
                    status.CurrentState == serviceRunning
                    ? status.ProcessId
                    : 0;
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint;
        public uint ProcessId, ServiceFlags;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenServiceW(IntPtr manager, string serviceName, uint desiredAccess);

    // InfoLevel 0 = SC_STATUS_PROCESS_INFO, which fills SERVICE_STATUS_PROCESS.
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, out ServiceStatusProcess status, int bufferSize, out int bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
