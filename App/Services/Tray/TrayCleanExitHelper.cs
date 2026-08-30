using System.Diagnostics;
using Lertaro.Core;
using Application = System.Windows.Application;

namespace Lertaro.App.Services.Tray;

internal static class TrayCleanExitHelper
{
    public static void CleanExit()
    {
        if (IsOnlyAppProcessRunning())
        {
            TryStopService();
        }

        Application.Current.Shutdown();
    }

    public static bool IsOnlyAppProcessRunning()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            var processes = Process.GetProcessesByName(current.ProcessName);
            try
            {
                return processes.Length == 1;
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[TrayCleanExitHelper] Failed to count app processes: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    private static void TryStopService()
    {
        try
        {
            // No elevation: the service grants START/STOP to authenticated users at install time, so a
            // normal-user stop succeeds without a UAC prompt. (Older installs lacking that grant just fail
            // here and the service keeps running, which is harmless.)
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "stop LertaroService",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            _ = proc;
        }
        catch (Exception ex)
        {
            Logger.Log($"[TrayCleanExitHelper] Failed to stop service: {ex.Message}", LogLevel.Warn);
        }
    }
}
