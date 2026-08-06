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
            var current = Process.GetCurrentProcess();
            return Process.GetProcessesByName(current.ProcessName).Length == 1;
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
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Logger.Log($"[TrayCleanExitHelper] Failed to stop service: {ex.Message}", LogLevel.Warn);
        }
    }
}
