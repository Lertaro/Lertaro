using Lertaro.Core;
using Lertaro.Core.Services;

using Lertaro.Core.Hook.Ipc;
using Lertaro.Core.Services.Plugin.Loading;
namespace Lertaro.Service;

// Hook-mode bootstrap: DPI awareness, plugin loading for path collectors, and the Win32 message loop that
// hosts the low-level keyboard/mouse hooks. Kept separate from Program's CLI dispatch and service
// install/uninstall -- none of those share a reason to change with this.
static class HookModeLauncher
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    // -4 is DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 (winuser.h).
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    public static void Run()
    {
        // Without this, this process defaults to DPI-unaware, so GetWindowRect/GetMonitorInfo return
        // coordinates virtualized down to 96 DPI while DwmGetWindowAttribute returns true physical
        // pixels -- the two stop matching at any scaling above 100%, which is why FullscreenHelper's
        // "does the foreground window's rect equal the monitor's rect" check silently failed for
        // fullscreen video at 150% scaling (reported bug: quick window still summonable during
        // PotPlayer fullscreen playback). Only hook mode calls FullscreenHelper, so this is scoped
        // here rather than in Main. Best-effort since the API is only available on Windows 10 1703+.
        try { SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { /* best-effort */ }

        var settings = UserSettings.Load();

        // Apply log level from settings
        if (Enum.TryParse<LogLevel>(settings.LogLevel, ignoreCase: true, out var logLevel))
            Logger.MinimumLevel = logLevel;

        // Load plugins to register path collectors in the hook process
        ServicePluginLoader.LoadForHook();

        // Both facts, because they are not the same one: administrator is a role in the token, High is the
        // label that decides whether this process's messages reach an elevated window. Saying only the first
        // made a Medium-integrity hook read as elevated in the log while the settings page called it
        // unelevated, and the two were both telling the truth.
        Logger.Log($"[HookMode] Starting hook process "
            + $"(integrity={ElevationManager.DescribeProcessIntegrity(Environment.ProcessId)}, "
            + $"admin={ElevationManager.IsRunningAsAdmin()}).");

        using var ipcServer = new HookIpcServer();
        using var hookProcess = new HookProcess(ipcServer);

        ipcServer.OnStopRequested += () =>
        {
            Logger.Log("[HookMode] Stop requested by App.");
            hookProcess.Stop();
        };

        ipcServer.Start();

        // Block on the Win32 message loop (installs hook inside)
        hookProcess.RunMessageLoop();

        Logger.Log("[HookMode] Message loop exited; shutting down hook mode.");
    }
}
