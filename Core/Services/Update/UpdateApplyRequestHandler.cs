using System.IO.Pipes;

using Lertaro.Core.Services.HookLaunch;

using Lertaro.Core.Services.Pipe;

using Lertaro.Core.Wire;
namespace Lertaro.Core.Services.Update;

/// <summary>
/// Handles SearchRequestId.ApplyUpdate: verifies a staged update package and hands it to an elevated
/// process in the caller's own session, which is how an update reaches Program Files without the App ever
/// owning a runas/UAC prompt of its own.
/// </summary>
/// <remarks>
/// This is the privileged half of the update, so it takes its answers from the kernel and from its own
/// location rather than from the request: who is asking comes from the pipe handle (same pattern as
/// <see cref="HookLaunchRequestHandler"/>), and where the files land is this process's own directory. The
/// one thing trusted from the payload is which staging directory to read, and everything read out of it has
/// to carry a signature this code verifies before it is unpacked.
///
/// The unpacked payload deliberately lands in a subdirectory of the install directory instead of the temp
/// directory it came from, and that is the load-bearing detail: the temp directory is writable by the
/// (unprivileged) user whose update this is, so an elevated copier reading from it would hand that user's
/// malware a way to write arbitrary files into Program Files. The install directory is not user-writable,
/// and it is the directory the copier was going to be pointed at anyway.
///
/// There is no separate consent check because there is nothing to check against: this process is LocalSystem
/// and the "auto silent update" preference lives in the interactive user's own settings file. Being this
/// install's Lertaro.App.exe is the consent, and the App only sends the request when the user asked for it.
/// Verifying before answering also earns its keep: a package that can't verify should fail while there is
/// still a window on screen to say so in, not after the App quit to let an updater run that then refused to
/// touch anything.
/// </remarks>
internal static class UpdateApplyRequestHandler
{
    private static readonly object UpdateGate = new();
    private static System.Diagnostics.Process? _applier;
    /// <summary>Subdirectory of the install directory the verified payload is unpacked into for the copier.</summary>
    internal const string PayloadStagingFolderName = "update-payload";

    public static PipeResponse Handle(NamedPipeServerStream pipe, string? sourceDir)
    {
        lock (UpdateGate) return HandleCore(pipe, sourceDir);
    }

    private static PipeResponse HandleCore(NamedPipeServerStream pipe, string? sourceDir)
    {
        try
        {
            if (!PipeClientIdentity.TryGetClientProcessId(pipe, out var callerPid) ||
                !PipeClientIdentity.TryGetClientSessionId(pipe, out var sessionId))
                return Reject("Unable to identify caller.");

            if (!PipeClientIdentity.IsAuthorizedApp(pipe, administrator: true))
                return Reject("Updating this installation requires an administrator account running this installation's App.");
            if (_applier is { HasExited: false }) return Reject("An update is already running.");
            _applier?.Dispose();
            _applier = null;

            var installDir = Path.TrimEndingDirectorySeparator(AppDomain.CurrentDomain.BaseDirectory);
            using var lease = AcquireUpdateLock(installDir);
            var updaterScript = Path.Combine(installDir, "portable-updater.ps1");
            if (!File.Exists(updaterScript))
                return Reject("Updater script is missing from this install.");

            // A leftover from a run that died between unpacking and copying would otherwise be copied over
            // as part of this one.
            var unpackDir = Path.Combine(installDir, PayloadStagingFolderName, Guid.NewGuid().ToString("N"));

            // Read as the caller, not as LocalSystem: the staging directory is the caller's to name, and
            // this process must not reach into it (or through a link in it, or out to a share) with its own
            // rights and credentials. What was read is then verified and unpacked from memory, so the files
            // can change afterwards without it mattering. Deleting the staging directory is the App's job;
            // it owns it, and a delete by LocalSystem of a path someone else chose is a delete of anything.
            byte[]? zip = null, signature = null;
            string? readError = null;
            var read = false;
            pipe.RunAsClient(() => read = UpdatePackage.TryReadStagedPackage(sourceDir, out zip, out signature, out readError));
            if (!read)
                return Reject(readError ?? "Unusable update package.");

            if (!UpdatePackage.TryVerifyAndExtract(zip!, signature!, unpackDir, out var payloadDir, out var packageError))
                return Reject(packageError ?? "Unusable update package.");

            // Run the script in the system PowerShell host, which holds none of our binaries open.
            //
            // Recorded before the copier starts, because the copier stops this service and this service is
            // the only process that can hand the App back to the session at its own integrity level.
            UpdateRelaunchMarker.Write(sessionId, DateTimeOffset.UtcNow);

            // No user profile, no PATH lookup for the host; the script also restricts module discovery.
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var arguments = $"-NoLogo -NoProfile -NonInteractive -File \"{updaterScript}\" -Source \"{payloadDir}\" -Destination \"{installDir}\"";
            var powershell = Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!SessionProcessLauncher.TryLaunch(sessionId, powershell, arguments, requestElevation: true,
                    detachFromConsole: false, out var pid, out var error))
            {
                // Nothing was copied and this service stays running, so the note would only make some later
                // start of the service launch an App nobody asked for.
                UpdateRelaunchMarker.Clear();
                return Reject(error ?? "Could not start the updater.");
            }

            // Covers the handoff until the updater opens the same lock file. It acquires that lock
            // before stopping the service and holds it through copy/rollback and service restart.
            _applier = System.Diagnostics.Process.GetProcessById(pid);

            Logger.Log($"[UsnService] Update applier launched (PID {pid}) into session {sessionId} for PID {callerPid}.");
            return new PipeResponse { Kind = PipeResponseKind.Ok };
        }
        catch (Exception ex)
        {
            Logger.Log($"[UsnService] ApplyUpdate error: {ex.Message}", LogLevel.Error);
            return new PipeResponse { Kind = PipeResponseKind.Error, Message = ex.Message };
        }
    }

    internal static FileStream AcquireUpdateLock(string installDirectory) =>
        new(Path.Combine(installDirectory, "update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static PipeResponse Reject(string reason)
    {
        // Loud on purpose: a rejection here is either a mispaired install or something trying to use the
        // service as a way to write files it cannot write itself.
        Logger.Log($"[UsnService] Rejected ApplyUpdate: {reason}", LogLevel.Warn);
        return new PipeResponse { Kind = PipeResponseKind.Error, Message = reason };
    }
}
