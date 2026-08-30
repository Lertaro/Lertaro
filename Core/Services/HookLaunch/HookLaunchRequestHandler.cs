using System.Diagnostics;
using System.IO.Pipes;

using Lertaro.Core.Services.Pipe;

using Lertaro.Core.Wire;
namespace Lertaro.Core.Services.HookLaunch;

// Split out of UsnServicePipeServer to keep that file under the line-count limit (mirrors PipeSecurityFactory).
// Handles SearchRequestId.LaunchHook: identifies the caller straight from the pipe's kernel handle (never
// trusts anything the client claims in the request itself), confirms it's genuinely this install's
// Lertaro.App.exe, then asks HookProcessBroker to spawn the hook into that caller's own session --
// elevated only if requestElevation is set AND that session's user is genuinely an administrator.
internal static class HookLaunchRequestHandler
{
    // The App-side HookIpcClient retries LaunchHook every 5 seconds, so one mispaired App install
    // (a debug or portable copy connecting to this installed service) would otherwise write an
    // identical rejection line every 5 seconds for as long as it runs. Warn once per caller PID,
    // and re-warn only after this interval so a lingering offender stays visible without spam.
    internal const long RejectionReLogIntervalMs = 10 * 60_000;
    private static readonly RejectionLogThrottle RejectionThrottle = new(RejectionReLogIntervalMs);

    public static PipeResponse Handle(NamedPipeServerStream pipe, bool requestElevation)
    {
        try
        {
            if (!PipeClientIdentity.TryGetClientProcessId(pipe, out var callerPid) ||
                !PipeClientIdentity.TryGetClientSessionId(pipe, out var sessionId))
                return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Unable to identify caller." };

            if (!IsGenuineAppProcess(callerPid))
            {
                if (RejectionThrottle.ShouldLog(callerPid, Environment.TickCount64))
                    Logger.Log($"[UsnService] Rejected LaunchHook: PID {callerPid} is not this install's Lertaro.App.exe.", LogLevel.Warn);
                return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Unauthorized caller." };
            }

            var hookExePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(hookExePath))
                return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Could not resolve service executable path." };

            if (!HookProcessBroker.TryLaunch(sessionId, hookExePath, "--hook", requestElevation, out var pid, out var error))
            {
                Logger.Log($"[UsnService] LaunchHook failed for session {sessionId}: {error}", LogLevel.Warn);
                return new PipeResponse { Kind = PipeResponseKind.Error, Message = error ?? "Launch failed." };
            }

            Logger.Log($"[UsnService] Launched hook (PID {pid}, elevation requested={requestElevation}) into session {sessionId}.");
            return new PipeResponse { Kind = PipeResponseKind.HookLaunched, Pid = pid };
        }
        catch (Exception ex)
        {
            Logger.Log($"[UsnService] LaunchHook error: {ex.Message}", LogLevel.Error);
            return new PipeResponse { Kind = PipeResponseKind.Error, Message = ex.Message };
        }
    }

    private static bool IsGenuineAppProcess(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            var callerPath = proc.MainModule?.FileName;
            if (string.IsNullOrEmpty(callerPath) ||
                !Path.GetFileName(callerPath).Equals("Lertaro.App.exe", StringComparison.OrdinalIgnoreCase))
                return false;

            var servicePath = Process.GetCurrentProcess().MainModule?.FileName;
            var serviceDir = string.IsNullOrEmpty(servicePath) ? null : Path.GetDirectoryName(Path.GetFullPath(servicePath));
            var callerDir = Path.GetDirectoryName(Path.GetFullPath(callerPath));
            return serviceDir != null && callerDir != null &&
                string.Equals(serviceDir, callerDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
