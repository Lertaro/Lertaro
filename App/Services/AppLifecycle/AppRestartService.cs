using System.Diagnostics;
using System.Globalization;
using Lertaro.Core;
using Application = System.Windows.Application;

namespace Lertaro.App.Services.AppLifecycle;

/// <summary>Coordinates a restart without racing the app's single-instance mutex.</summary>
public static class AppRestartService
{
    private const string WaitForProcessArgument = "--lertaro-restart-wait-pid=";
    private const int ParentWaitTimeoutMilliseconds = 30000;
    private static int _restartRequested;

    public static bool RequestRestart()
    {
        var application = Application.Current;
        if (application == null || application.Dispatcher.HasShutdownStarted)
            return false;
        if (Interlocked.Exchange(ref _restartRequested, 1) != 0)
            return true;

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            Interlocked.Exchange(ref _restartRequested, 0);
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add(WaitForProcessArgument + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            if (Process.Start(startInfo) == null)
                throw new InvalidOperationException("The replacement process could not be started.");

            application.Dispatcher.BeginInvoke(new Action(application.Shutdown));
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[AppRestartService] Failed to start replacement process: {ex.Message}", LogLevel.Error);
            Interlocked.Exchange(ref _restartRequested, 0);
            return false;
        }
    }

    public static void WaitForParentExit(IReadOnlyList<string> args)
    {
        if (!TryGetParentProcessId(args, out var parentProcessId) || parentProcessId == Environment.ProcessId)
            return;

        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            parent.WaitForExit(ParentWaitTimeoutMilliseconds);
        }
        catch (ArgumentException)
        {
            // The parent already exited before the replacement opened its process handle.
        }
        catch (InvalidOperationException)
        {
            // The process exited while its handle was being opened.
        }
    }

    internal static bool TryGetParentProcessId(IReadOnlyList<string> args, out int processId)
    {
        foreach (var argument in args)
        {
            if (!argument.StartsWith(WaitForProcessArgument, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = argument[WaitForProcessArgument.Length..];
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out processId)
                && processId > 0)
                return true;
        }

        processId = 0;
        return false;
    }
}
