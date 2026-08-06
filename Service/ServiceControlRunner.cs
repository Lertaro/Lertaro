using System.Diagnostics;
using Lertaro.Core;

namespace Lertaro.Service;

internal static class ServiceControlRunner
{
    private const int TimeoutMs = 30000;

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
                return LogResult(new ServiceCommandResult(arguments, null, true, string.Empty, $"Timed out after {TimeoutMs}ms."), successExitCodes);
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
