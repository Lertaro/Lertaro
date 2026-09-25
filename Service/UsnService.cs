using System.Diagnostics;
using System.ServiceProcess;
using Lertaro.Core;
using Lertaro.Core.Services;

using Lertaro.Core.Services.HookLaunch;

using Lertaro.Core.Services.Plugin.Loading;

using Lertaro.Core.Services.Update;
namespace Lertaro.Service;

public class UsnService : ServiceBase
{
    private SearchEngine? _engine;
    private UsnServicePipeServer? _pipeServer;

    public UsnService()
    {
        ServiceName = "LertaroService";
        CanStop = true;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        Logger.Log("[UsnService] Service Starting...");
        try
        {
            // Local-drive scanning (USN/MFT/ReFS full builds, the non-USN LocalDriveWalkBuilder fallback,
            // and their FileSystemWatcher-based file monitors) all run in this same process. Unlike
            // network-drive indexing, they're already isolated from the App's own ThreadPool by being in
            // a separate process, but their threads still compete for physical CPU with the App's UI
            // thread through the OS scheduler. BelowNormal covers all of that background work uniformly
            // (rather than touching every scanner class individually) and only matters under real
            // contention -- an idle system still runs this service at full speed, so a full drive rebuild
            // isn't slowed down. The pipe server handling search queries lives in this process too and
            // inherits the same priority, which is the right trade-off: a UI frame is more urgent than a
            // search reply that's already going through IPC latency regardless.
            using var currentProcess = Process.GetCurrentProcess();
            currentProcess.PriorityClass = ProcessPriorityClass.BelowNormal;

            ServicePluginLoader.LoadForService();
            _engine = new SearchEngine();
            _engine.InitializeOrLoadIndex(false);

            _pipeServer = new UsnServicePipeServer();
            _pipeServer.Start(_engine);
            RelaunchAppAfterUpdate();
            Logger.Log("[UsnService] Service Started successfully.");
        }
        catch (Exception ex)
        {
            Logger.Log($"[UsnService] Failed to start service: {ex}", LogLevel.Error);
            Stop();
        }
    }

    protected override void OnStop()
    {
        Logger.Log("[UsnService] Service Stopping...");
        _pipeServer?.Stop();
        _pipeServer?.Dispose();
        _pipeServer = null;

        _engine?.Dispose();
        _engine = null;
        Logger.Log("[UsnService] Service Stopped.");
    }

    protected override void OnShutdown()
    {
        OnStop();
        base.OnShutdown();
    }

    /// <summary>
    /// Hands the App back to the user after a silent update.
    /// </summary>
    /// <remarks>
    /// The copier runs elevated, and an elevated process asking the session's (non-elevated) shell to start
    /// something is dropped by UI Privilege Isolation -- measured on the machine this replaced: the files
    /// were copied, the service was left stopped, and the App never came back. Only this process, restarted
    /// by the copier, holds the privilege to start the App at the session's own integrity level, which is
    /// what the note written before the copy is for.
    ///
    /// Called after the pipe server is listening, so an App that comes up and asks the service something
    /// immediately finds an answer. A duplicate for a user who started the App by hand in the meantime is
    /// answered by the App's own per-session mutex.
    /// </remarks>
    private static void RelaunchAppAfterUpdate()
    {
        if (!UpdateRelaunchMarker.TryTake(out var sessionId, out var appExePath, DateTimeOffset.UtcNow))
            return;

        if (!SessionProcessLauncher.TryLaunch(sessionId, appExePath, string.Empty, requestElevation: false,
                detachFromConsole: true, out var pid, out var error))
        {
            Logger.Log($"[UsnService] Update finished but the App could not be started: {error}", LogLevel.Error);
            return;
        }

        Logger.Log($"[UsnService] Started the updated App (PID {pid}) in session {sessionId}.");
    }

    internal void TestStart() => OnStart(Array.Empty<string>());
    internal void TestStop() => OnStop();
}
