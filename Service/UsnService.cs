using System.Diagnostics;
using System.ServiceProcess;
using Lertaro.Core;
using Lertaro.Core.Services;

using Lertaro.Core.Services.Plugin.Loading;
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
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;

            ServicePluginLoader.LoadForService();
            _engine = new SearchEngine();
            _engine.InitializeOrLoadIndex(false);

            _pipeServer = new UsnServicePipeServer();
            _pipeServer.Start(_engine);
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

    internal void TestStart() => OnStart(Array.Empty<string>());
    internal void TestStop() => OnStop();
}
