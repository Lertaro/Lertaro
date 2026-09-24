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
    /// <summary>Subdirectory of the install directory the verified payload is unpacked into for the copier.</summary>
    internal const string PayloadStagingFolderName = "update-payload";

    public static PipeResponse Handle(NamedPipeServerStream pipe, string? sourceDir)
    {
        try
        {
            if (!PipeClientIdentity.TryGetClientProcessId(pipe, out var callerPid) ||
                !PipeClientIdentity.TryGetClientSessionId(pipe, out var sessionId))
                return Reject("Unable to identify caller.");

            if (!HookLaunchRequestHandler.IsGenuineAppProcess(callerPid))
                return Reject($"PID {callerPid} is not this install's Lertaro.App.exe.");

            var installDir = Path.TrimEndingDirectorySeparator(AppDomain.CurrentDomain.BaseDirectory);
            var updaterBat = Path.Combine(installDir, "portable-updater.bat");
            if (!File.Exists(updaterBat))
                return Reject("Updater script is missing from this install.");

            // A leftover from a run that died between unpacking and copying would otherwise be copied over
            // as part of this one.
            var unpackDir = Path.Combine(installDir, PayloadStagingFolderName);
            if (Directory.Exists(unpackDir))
                Directory.Delete(unpackDir, true);

            if (!UpdatePackage.TryVerifyAndExtract(sourceDir, unpackDir, out var payloadDir, out var packageError))
                return Reject(packageError ?? "Unusable update package.");

            // The payload now sits where the copier will read it from, so the downloaded zip and signature
            // have served their purpose. Best effort: this is the last moment anything knows where they are.
            TryDeleteDirectory(sourceDir!);

            // cmd.exe rather than a copy of this executable, because whatever does the copying must not be
            // one of the files being copied: an applier running from the install directory would hold
            // Lertaro.Service.exe and Lertaro.Core.dll locked and could not overwrite them. The script lives
            // in System32, so nothing it replaces is in use by it.
            var arguments = $"/c \"\"{updaterBat}\" \"{payloadDir}\" \"{installDir}\"\"";
            var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            if (!SessionProcessLauncher.TryLaunch(sessionId, cmdExe, arguments, requestElevation: true,
                    detachFromConsole: false, out var pid, out var error))
                return Reject(error ?? "Could not start the updater.");

            Logger.Log($"[UsnService] Update applier launched (PID {pid}) into session {sessionId} for PID {callerPid}.");
            return new PipeResponse { Kind = PipeResponseKind.Ok };
        }
        catch (Exception ex)
        {
            Logger.Log($"[UsnService] ApplyUpdate error: {ex.Message}", LogLevel.Error);
            return new PipeResponse { Kind = PipeResponseKind.Error, Message = ex.Message };
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (Exception ex)
        {
            Logger.Log($"[UsnService] Could not clean up {path}: {ex.Message}", LogLevel.Warn);
        }
    }

    private static PipeResponse Reject(string reason)
    {
        // Loud on purpose: a rejection here is either a mispaired install or something trying to use the
        // service as a way to write files it cannot write itself.
        Logger.Log($"[UsnService] Rejected ApplyUpdate: {reason}", LogLevel.Warn);
        return new PipeResponse { Kind = PipeResponseKind.Error, Message = reason };
    }
}
