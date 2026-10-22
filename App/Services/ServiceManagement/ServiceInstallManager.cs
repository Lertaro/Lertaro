using System.Diagnostics;
using System.IO;
using Lertaro.Core;
using Lertaro.Core.Services.Installation;

namespace Lertaro.App.Services;

public static class ServiceInstallManager
{
    public static void RepairPermissions()
    {
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        // The elevated repair cannot have its stderr captured, and it deliberately runs before the service
        // opens its log: it records its steps in this file instead, which is read back below. See
        // PermissionRepairReport for why nothing else works on this path.
        PermissionRepairReport.Clear();
        using var process = Process.Start(new ProcessStartInfo(GetServiceExePath(), $"--repair-permissions {sid}")
        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })
            ?? throw new IOException("Could not start the permission repair.");
        var exited = process.WaitForExit(InstallerTimeoutMs);
        if (!exited || process.ExitCode != 0)
            throw new IOException(DescribeRepairFailure(exited, exited ? process.ExitCode : 0, PermissionRepairReport.Read(), sid));
    }

    /// <summary>What the user is told when the elevated repair did not succeed. Pure, so it can be asserted.</summary>
    /// <remarks>
    /// "Permission repair did not complete successfully" was the whole message this used to produce, for a
    /// timeout, a refused lock walk, an unknown profile and a thrown exception alike (issue #342).
    /// </remarks>
    internal static string DescribeRepairFailure(bool exited, int exitCode, string? report, string sid)
    {
        var reason = exited
            ? $"Permission repair failed (exit code {exitCode})."
            : $"Permission repair did not finish within {InstallerTimeoutMs / 1000} seconds; it was still at: "
              + $"{PermissionRepairReport.LastStep(report) ?? "an unrecorded step"}.";
        var detail = TailOf(report) ?? "It left no diagnostic of its own.";
        return $"{reason}\n{detail}\nThe whole trace is in {PermissionRepairReport.DefaultPath}\n"
            + $"To see the failure directly, run \"{GetServiceExePath()}\" --repair-permissions {sid} "
            + "from an elevated command prompt.";
    }

    /// <summary>What the user is told when the elevated installer did not succeed.</summary>
    /// <remarks>
    /// Same shape as <see cref="DescribeRepairFailure"/>, and for the same reason: the installer's stderr is
    /// discarded too (UseShellExecute + runas, hidden window), its log opens only after the steps that fail
    /// first, and "did not complete successfully (declined, timed out, or failed)" is three different problems
    /// in one sentence. A service that never starts is usually this message and nothing else.
    /// </remarks>
    internal static string DescribeInstallFailure(bool exited, int exitCode, string? report, string serviceExePath)
    {
        var reason = exited
            ? $"Service installation failed (exit code {exitCode})."
            : $"Service installation did not finish within {InstallerTimeoutMs / 1000} seconds; it was still at: "
              + $"{PermissionRepairReport.LastStep(report) ?? "an unrecorded step"}.";
        var detail = TailOf(report) ?? "It left no diagnostic of its own.";
        return $"{reason}\n{detail}\nThe whole trace is in {PermissionRepairReport.DefaultPath}\n"
            + $"To see the failure directly, run \"{serviceExePath}\" --install from an elevated command prompt.";
    }

    /// <summary>
    /// The last few lines of the repair's trace: enough to show what happened, not a scrolling box. The file
    /// itself is capped at 16 KB, which is still far more than a message box should ever be asked to show.
    /// </summary>
    internal static string? TailOf(string? report, int lines = MaximumReportLines)
    {
        if (string.IsNullOrWhiteSpace(report)) return null;

        var all = report.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return all.Length == 0 ? null : string.Join("\n", all[^Math.Min(lines, all.Length)..]);
    }

    private const int MaximumReportLines = 6;

    // Raised from 30 s: --repair-permissions performs the same install-directory lock walk as service startup
    // (see Program.LockDirectories), so the old budget no longer described the work it was bounding.
    private const int InstallerTimeoutMs = 120000;
    private const int StartTimeoutMs = 10000;
    private static int _silentInstallInFlight;

    public enum SilentInstallResult
    {
        // The install/start sequence ran; onCompleted has fired.
        Started,
        // Another silent install is already in flight; no callbacks fired.
        AlreadyRunning,
        // The install or registration check failed; onFailed has fired.
        Failed
    }

    public static string GetServiceExePath()
    {
        var serviceExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lertaro.Service.exe");
        if (!File.Exists(serviceExePath))
        {
            serviceExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\Service\bin\Debug\net10.0-windows\Lertaro.Service.exe");
        }
        return Path.GetFullPath(serviceExePath);
    }

    public static void InstallService(Action onCompleted, Action<Exception> onError)
    {
        try
        {
            var serviceExePath = GetServiceExePath();
            Logger.Log($"[ServiceInstallManager] Requesting service installation: {serviceExePath} --install");
            if (RunElevatedInstaller("Service installation", serviceExePath, out var diagnostic))
                onCompleted?.Invoke();
            else
                onError?.Invoke(new InvalidOperationException(
                    diagnostic ?? "Service installation did not complete successfully."));
        }
        catch (Exception ex)
        {
            Logger.Log($"[ServiceInstallManager] Service installation failed: {ex}", LogLevel.Error);
            onError?.Invoke(ex);
        }
    }

    // Result-shape note: exactly one of onCompleted/onFailed fires when the result is Started or
    // Failed, and neither fires for AlreadyRunning. The old bool return collapsed "installer failed"
    // (UAC declined, nonzero exit, service not registered) into the same true as success, so callers
    // could only tell "in flight" apart from everything else -- a failed install looked completed to
    // the UI and only surfaced at the next failed ping.
    public static SilentInstallResult SilentInstall(Action onCompleted, Action<Exception>? onFailed = null)
    {
        if (Interlocked.CompareExchange(ref _silentInstallInFlight, 1, 0) != 0)
        {
            Logger.Log("[ServiceInstallManager] Silent service installation already in progress.", LogLevel.Debug);
            return SilentInstallResult.AlreadyRunning;
        }

        try
        {
            var serviceExePath = GetServiceExePath();
            Logger.Log($"[ServiceInstallManager] Attempting silent service installation: {serviceExePath}");
            if (!RunElevatedInstaller("Silent service installation", serviceExePath, out var diagnostic))
            {
                onFailed?.Invoke(new InvalidOperationException(diagnostic
                    ?? "Silent service installation did not complete successfully (declined, timed out, or failed)."));
                return SilentInstallResult.Failed;
            }

            var isRegistered = IsInstalledAtCurrentPath();
            var startSucceeded = isRegistered && TryStartWithoutElevation();
            var result = ServiceInstallOutcomeHelper.DetermineResult(
                installerSucceeded: true,
                registeredAtCurrentPath: isRegistered,
                serviceStarted: startSucceeded);
            if (result == SilentInstallResult.Failed)
            {
                var message = !isRegistered
                    ? "LertaroService is not registered at the current service path after installation."
                    : "LertaroService was installed but could not be started.";
                Logger.Log($"[ServiceInstallManager] {message}", LogLevel.Error);
                onFailed?.Invoke(new InvalidOperationException(message));
                return result;
            }

            Logger.Log("[ServiceInstallManager] LertaroService is registered at the current path and start command succeeded.");
        }
        catch (Exception ex)
        {
            Logger.Log($"[ServiceInstallManager] Silent service installation failed: {ex.Message}", LogLevel.Error);
            onFailed?.Invoke(ex);
            return SilentInstallResult.Failed;
        }
        finally
        {
            Interlocked.Exchange(ref _silentInstallInFlight, 0);
        }

        onCompleted?.Invoke();
        return SilentInstallResult.Started;
    }

    private static bool RunElevatedInstaller(string operation, string serviceExePath, out string? diagnostic)
    {
        diagnostic = null;
        var psi = new ProcessStartInfo
        {
            FileName = serviceExePath,
            Arguments = "--install",
            Verb = "runas",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        // The elevated process cannot answer us once it runs -- nothing about its stdout or stderr survives
        // UseShellExecute -- so it records its steps in this file and the reason is read back from there.
        PermissionRepairReport.Clear();
        using var proc = Process.Start(psi);
        if (proc == null)
        {
            Logger.Log($"[ServiceInstallManager] {operation} failed: Process.Start returned null.", LogLevel.Error);
            return false;
        }

        if (!proc.WaitForExit(InstallerTimeoutMs))
        {
            Logger.Log($"[ServiceInstallManager] {operation} timed out after {InstallerTimeoutMs}ms.", LogLevel.Error);
            TryKill(proc);
            diagnostic = DescribeInstallFailure(exited: false, exitCode: 0, PermissionRepairReport.Read(), serviceExePath);
            Logger.Log($"[ServiceInstallManager] {diagnostic}", LogLevel.Error);
            return false;
        }

        Logger.Log($"[ServiceInstallManager] {operation} exited with code {proc.ExitCode}.");
        if (proc.ExitCode == 0)
            return true;

        // The reason the service never came up, in the words of the process that knows it. Without this the
        // caller only had the exit code, and the user only had "1053".
        diagnostic = DescribeInstallFailure(exited: true, exitCode: proc.ExitCode, PermissionRepairReport.Read(), serviceExePath);
        Logger.Log($"[ServiceInstallManager] {diagnostic}", LogLevel.Error);
        return false;
    }

    /// <summary>
    /// True when LertaroService is registered and its binary is exactly the exe this build would
    /// install (same full path). A stale path (old version / moved folder) returns false so it gets
    /// reinstalled rather than started against the wrong binary.
    /// </summary>
    public static bool IsInstalledAtCurrentPath()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\LertaroService");
            if (key?.GetValue("ImagePath") is not string imagePath || string.IsNullOrWhiteSpace(imagePath))
                return false;

            var installedExe = ExtractExePath(imagePath);
            if (string.IsNullOrEmpty(installedExe))
                return false;

            return string.Equals(
                Path.GetFullPath(installedExe),
                Path.GetFullPath(GetServiceExePath()),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Logger.Log($"[ServiceInstallManager] Failed to read service ImagePath: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    private static string ExtractExePath(string imagePath)
    {
        imagePath = imagePath.Trim();
        if (imagePath.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = imagePath.IndexOf('"', 1);
            return end > 0 ? imagePath.Substring(1, end - 1) : imagePath.Trim('"');
        }
        // Unquoted ImagePath: take up to and including ".exe".
        var exeIdx = imagePath.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeIdx >= 0 ? imagePath.Substring(0, exeIdx + 4) : imagePath;
    }

    /// <summary>
    /// Starts the service without elevation, relying on the START permission granted to authenticated
    /// users at install time. Returns true if the service is running afterwards.
    /// </summary>
    public static bool TryStartWithoutElevation()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "start LertaroService",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null)
                return false;
            if (!proc.WaitForExit(StartTimeoutMs))
            {
                Logger.Log($"[ServiceInstallManager] Non-elevated start timed out after {StartTimeoutMs}ms.", LogLevel.Warn);
                TryKill(proc);
                return false;
            }
            // 0 = started; 1056 = ERROR_SERVICE_ALREADY_RUNNING.
            var success = proc.ExitCode == 0 || proc.ExitCode == 1056;
            Logger.Log($"[ServiceInstallManager] Non-elevated start exited with code {proc.ExitCode}.", success ? LogLevel.Info : LogLevel.Warn);
            return success;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ServiceInstallManager] Non-elevated start failed: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    /// <summary>
    /// Fast path before falling back to an elevated (re)install: if the service is already installed
    /// pointing at this build's exe, just start it without a UAC prompt.
    /// </summary>
    public static bool TryStartExistingService()
        => IsInstalledAtCurrentPath() && TryStartWithoutElevation();

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }
}
