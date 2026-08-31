using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using Lertaro.Core;

namespace Lertaro.Service;

internal static class ServiceControlRunner
{
    private const int TimeoutMs = 30000;
    private const int ServicePollIntervalMs = 100;

    public static ServiceCommandResult Run(string arguments, params int[] successExitCodes)
    {
        successExitCodes = successExitCodes.Length == 0 ? [0] : successExitCodes;
        Logger.Log($"[ServiceInstaller] Running: sc.exe {arguments}");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return LogResult(new ServiceCommandResult(arguments, null, false, string.Empty, "Process.Start returned null."), successExitCodes);

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeoutMs))
            {
                TryKill(process);
                var stdout = string.Empty;
                var stderr = string.Empty;
                try
                {
                    Task.WaitAll(new[] { stdoutTask, stderrTask }, TimeSpan.FromSeconds(1));
                    if (stdoutTask.IsCompletedSuccessfully) stdout = stdoutTask.Result.Trim();
                    if (stderrTask.IsCompletedSuccessfully) stderr = stderrTask.Result.Trim();
                }
                catch
                {
                    // Output tasks may fault if the process was killed before producing complete output.
                }

                return LogResult(new ServiceCommandResult(arguments, null, true, stdout, string.IsNullOrWhiteSpace(stderr) ? $"Timed out after {TimeoutMs}ms." : stderr), successExitCodes);
            }

            var result = new ServiceCommandResult(
                arguments,
                process.ExitCode,
                false,
                stdoutTask.GetAwaiter().GetResult().Trim(),
                stderrTask.GetAwaiter().GetResult().Trim());
            return LogResult(result, successExitCodes);
        }
        catch (Exception ex)
        {
            return LogResult(new ServiceCommandResult(arguments, null, false, string.Empty, ex.Message), successExitCodes);
        }
    }

    public static bool WaitForStopped(string serviceName)
        => WaitForStatus(serviceName, ServiceControllerStatus.Stopped, allowMissing: true);

    public static bool WaitForDeleted(string serviceName)
    {
        var deadline = Environment.TickCount64 + TimeoutMs;
        while (true)
        {
            try
            {
                using var service = new ServiceController(serviceName);
                service.Refresh();
                _ = service.Status;
            }
            catch (Exception ex) when (IsServiceMissing(ex))
            {
                return true;
            }

            if (Environment.TickCount64 >= deadline)
                return false;

            Thread.Sleep(ServicePollIntervalMs);
        }
    }

    private static bool WaitForStatus(string serviceName, ServiceControllerStatus expected, bool allowMissing)
    {
        var deadline = Environment.TickCount64 + TimeoutMs;
        while (true)
        {
            try
            {
                using var service = new ServiceController(serviceName);
                service.Refresh();
                if (service.Status == expected)
                    return true;
            }
            catch (Exception ex) when (allowMissing && IsServiceMissing(ex))
            {
                return true;
            }

            if (Environment.TickCount64 >= deadline)
                return false;

            Thread.Sleep(ServicePollIntervalMs);
        }
    }

    private static bool IsServiceMissing(Exception exception)
        => exception is Win32Exception { NativeErrorCode: 1060 }
            || exception.InnerException is Win32Exception { NativeErrorCode: 1060 };

    private static ServiceCommandResult LogResult(ServiceCommandResult result, int[] successExitCodes)
    {
        var ok = result.IsSuccess(successExitCodes);
        var level = ok ? LogLevel.Info : LogLevel.Error;
        Logger.Log($"[ServiceInstaller] sc.exe {result.Arguments} exit={result.ExitCode?.ToString() ?? "none"} timeout={result.TimedOut}", level);
        if (!string.IsNullOrWhiteSpace(result.Output))
            Logger.Log($"[ServiceInstaller] stdout: {result.Output}", LogLevel.Info);
        if (!string.IsNullOrWhiteSpace(result.Error))
            Logger.Log($"[ServiceInstaller] stderr: {result.Error}", ok ? LogLevel.Warn : LogLevel.Error);
        return result;
    }

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

internal sealed record ServiceCommandResult(string Arguments, int? ExitCode, bool TimedOut, string Output, string Error)
{
    public bool IsSuccess(params int[] successExitCodes)
        => !TimedOut && ExitCode.HasValue && successExitCodes.Contains(ExitCode.Value);
}
