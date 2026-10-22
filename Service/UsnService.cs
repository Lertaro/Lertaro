using System.Diagnostics;
using System.ServiceProcess;
using Lertaro.Core;
using Lertaro.Core.Services;

using Lertaro.Core.Services.HookLaunch;

using Lertaro.Core.Services.Installation;

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
        // The SCM kills a start that has not reported back within 30 s, and records only error 1053 when it
        // does. Locking a portable copy's folder is the one step here that can take minutes (a first start, or
        // any start after one that was cut short), so the time is asked for before the work begins rather than
        // discovered halfway through it.
        TryRequestAdditionalTime(ApplicationLockTimeoutMs);
        Logger.Log("[UsnService] Service Starting...");
        try
        {
            // Before the plugins, the index and the pipe: everything below runs out of this folder. Program
            // records why it refused in PermissionRepairReport, which is what the App shows.
            if (!Program.EnsureApplicationDirectoryLocked())
                throw new InvalidOperationException(
                    $"The folder this service runs from could not be locked ({AppContext.BaseDirectory}). "
                    + $"See {PermissionRepairReport.DefaultPath}.");

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
            _pipeServer.Start(_engine, Stop);
            RelaunchAppAfterUpdate();
            Logger.Log("[UsnService] Service Started successfully.");

            // Warm the search path while nobody is waiting for it, then pre-answer the queries a user is
            // most likely to type first.
            //
            // The first half used to be the whole warm-up and it was not enough: the scan is bound by the
            // index pages being resident, so a query still cost 40-450 ms per drive whenever those pages had
            // been handed back to the OS -- measured on this machine, and the 40-450 ms landed straight on
            // the user's first keystroke. Running one query only kept the pages it touched.
            //
            // So this walks every single letter instead. Each result is kept by IndexV2Searcher's query cache,
            // so the keystroke that used to pay the scan now replays it. The rows never reach a client and the
            // service records no search history (the App does), so nothing here looks like a search the user
            // made.
            //
            // It has to wait for the index first: run right after Start, the indexes are still empty, so the
            // search returns in 0 ms with 0 rows and warms nothing (measured exactly that).
            // One query: run it, keep whatever the query cache keeps, and report how many rows it
            // produced. Rows are discarded here -- the point is the cache entry, not the answer.
            // Re-enabled after an A/B run: with the warm-up off the first searches were measurably
            // slower while later ones were unchanged, so the scan it hides does land on the user.
            const bool WarmUpEnabled = true;

            int Warm(string query)
            {
                if (!WarmUpEnabled)
                    return 0;
                var produced = 0;
                try
                {
                    _engine?.SearchStreaming(query, 200, 20, null, _ => produced++, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    // One query failing must not end the warm-up: measured, the whole run was
                    // cancelled 8.4 s in by a real search arriving mid-way, which left the last
                    // letters cold and made the first keystroke slow or fast depending on what
                    // the user did after launch.
                    Lertaro.Core.SearchIndex.PerfTrace.Mark("warm.fail", Lertaro.Core.SearchIndex.PerfTrace.Begin(), 0);
                }

                return produced;
            }

            _ = Task.Factory.StartNew(async () =>
            {
                // Actually below normal, which is what the comment below always claimed: LongRunning only
                // makes a dedicated thread, it does not lower anything, so the warm-up ran at Normal and
                // competed with the search the user was typing. The first keystroke of a session lands
                // inside this window and felt heavy because of it.
                try
                {
                    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                }
                catch
                {
                }
                var started = Environment.TickCount64;
                try
                {
                    var deadline = started + 120_000;
                    while (Environment.TickCount64 < deadline)
                    {
                        var status = _engine?.GetStatus();
                        var loaded = status?.Drives?.Count(d =>
                            string.Equals(d.State, "ready", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(d.State, "cached", StringComparison.OrdinalIgnoreCase)) ?? 0;
                        if (loaded > 0)
                            break;
                        await Task.Delay(500).ConfigureAwait(false);
                    }

                    var searchedAt = Environment.TickCount64;
                    var warmed = 0;
                    var rows = 0;
                    // Serially, on purpose. Four at a time was tried and was worse: every query reads
                    // the same drive index, so the four competed for the read lock and the memory
                    // bandwidth the scan is bound by, which made the warm-up slower and stole CPU
                    // from the summon handshake at the same time.
                    // was time the summon handshake could not get a reply. Four at a time keeps
                    // every core busy without starving the hook/pipe work the double-Ctrl waits on.
                    // Two waves. The first letter a session asks for is the one the user is waiting on,
                    // so the letters people actually start with are warmed immediately; the rest follow
                    // once the app has been up long enough that nobody is looking at the first rows any
                    // more. Warming all 36 up front took ~7 s, and every search that landed inside that
                    // window paid the scan that had not been cached yet.
                    var firstWave = new[] { "a", "b", "c", "d", "e", "f", "g", "m", "p", "s", "t", "w" };
                    var letters = new List<string>(36);
                    for (var c = '0'; c <= '9'; c++)
                        letters.Add(c.ToString());
                    for (var c = 'a'; c <= 'z'; c++)
                        letters.Add(c.ToString());

                    foreach (var letter in firstWave)
                        rows += Warm(letter);
                    var firstWaveDone = Environment.TickCount64;
                    Logger.Log($"[UsnService] Search warm-up first wave finished in {firstWaveDone - searchedAt} ms ({firstWave.Length} queries).", LogLevel.Info);

                    // Let the app settle before the long tail: 30 s is past the moment the user
                    // either started using the first wave or walked away, so the extra scans land on
                    // an idle service instead of on the search they were waiting for.
                    await Task.Delay(30_000).ConfigureAwait(false);
                    foreach (var letter in letters)
                    {
                        if (Array.IndexOf(firstWave, letter) >= 0)
                            continue;
                        rows += Warm(letter);
                    }
                    warmed = letters.Count;

                    Logger.Log($"[UsnService] Search warm-up finished in {Environment.TickCount64 - searchedAt} ms after waiting {searchedAt - started} ms for the index ({warmed} queries pre-answered, {rows} rows discarded).", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[UsnService] Search warm-up failed after {Environment.TickCount64 - started} ms: {ex.Message}", LogLevel.Info);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
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

        HookProcessBroker.StopAll();

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
        if (!UpdateRelaunchMarker.TryTake(out var sessionId, DateTimeOffset.UtcNow))
            return;

        // Always this install's own App, never a path from the note: see UpdateRelaunchMarker.TryTake.
        var appExePath = Path.Combine(AppContext.BaseDirectory, "Lertaro.App.exe");
        if (!File.Exists(appExePath))
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

    // Budget for the lock walk, asked for before it starts. Larger than the walk ever needs on a normal copy so
    // that a slow disk degrades into a slow start rather than into the 1053 that has no explanation.
    private const int ApplicationLockTimeoutMs = 120_000;

    // RequestAdditionalTime throws when this process is not being driven by the service dispatcher, which is
    // how the debug console host (UsnServiceDebugWrapper) runs it.
    private void TryRequestAdditionalTime(int milliseconds)
    {
        try
        {
            RequestAdditionalTime(milliseconds);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
