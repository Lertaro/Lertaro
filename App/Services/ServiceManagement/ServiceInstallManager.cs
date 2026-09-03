using System.Diagnostics;
using System.IO;
using Lertaro.Core;

namespace Lertaro.App.Services;

public static class ServiceInstallManager
{
    private const int InstallerTimeoutMs = 30000;
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
            if (RunElevatedInstaller("Service installation", serviceExePath))
                onCompleted?.Invoke();
            else
                onError?.Invoke(new InvalidOperationException("Service installation did not complete successfully."));
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
            if (!RunElevatedInstaller("Silent service installation", serviceExePath))
            {
                onFailed?.Invoke(new InvalidOperationException("Silent service installation did not complete successfully (declined, timed out, or failed)."));
                return SilentInstallResult.Failed;
            }

            if (!IsInstalledAtCurrentPath())
            {
                Logger.Log("[ServiceInstallManager] Silent install finished but LertaroService is not registered at the current service path.", LogLevel.Error);
                onFailed?.Invoke(new InvalidOperationException("LertaroService is not registered at the current service path after installation."));
                return SilentInstallResult.Failed;
            }

            if (TryStartWithoutElevation())
                Logger.Log("[ServiceInstallManager] LertaroService is registered at the current path and start command succeeded.");
            else
                Logger.Log("[ServiceInstallManager] LertaroService is registered at the current path but start command failed.", LogLevel.Warn);
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

    private static bool RunElevatedInstaller(string operation, string serviceExePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = serviceExePath,
            Arguments = "--install",
            Verb = "runas",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

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
            return false;
        }

        Logger.Log($"[ServiceInstallManager] {operation} exited with code {proc.ExitCode}.");
        return proc.ExitCode == 0;
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
