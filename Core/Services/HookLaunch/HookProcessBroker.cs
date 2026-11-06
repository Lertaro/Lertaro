using System.Collections.Concurrent;
using System.Diagnostics;

namespace Lertaro.Core.Services.HookLaunch;

// Runs inside the SYSTEM-privileged --service process. Every hook launch -- elevated or not -- goes
// through here rather than the App spawning its own child process, so the App never has to hold a
// runas/UAC fallback of its own. The actual cross-session launch is SessionProcessLauncher's; this type
// adds the two things specific to hooks: a per-session record of the live process, so repeated requests
// don't spawn duplicates, and the rule that a record only answers a request at the level it actually is.
public static class HookProcessBroker
{
    private static readonly ConcurrentDictionary<int, Process> _liveHooks = new();
    private static readonly object _liveHooksGate = new();
    private static bool _stopping;

    public static bool TryLaunch(int sessionId, string exePath, string arguments, bool requestElevation, out int pid, out string? error)
    {
        pid = 0;
        error = null;

        lock (_liveHooksGate)
        {
            if (_stopping)
            {
                error = "The service is stopping.";
                return false;
            }
            if (_liveHooks.TryGetValue(sessionId, out var existing))
            {
                var live = false;
                try { live = !existing.HasExited; }
                catch { /* process object stale; fall through and relaunch */ }

                if (live)
                {
                    if (KeepLiveHook(requestElevation,
                            () => ElevationManager.IsProcessHighIntegrity(existing.Id),
                            () => SessionProcessLauncher.CanGrantElevation(sessionId)))
                    {
                        pid = existing.Id;
                        return true;
                    }
                    StopLiveHook(sessionId, existing);
                }
                else if (_liveHooks.TryRemove(sessionId, out var removed))
                    removed.Dispose();
            }

            if (!SessionProcessLauncher.TryLaunch(sessionId, exePath, arguments, requestElevation, detachFromConsole: true, out pid, out error))
                return false;

            try
            {
                var newProcess = Process.GetProcessById(pid);
                _ = newProcess.SafeHandle; // Pin the launched process, so a later PID reuse cannot target another one.
                if (_liveHooks.TryGetValue(sessionId, out var previous))
                    previous.Dispose();
                _liveHooks[sessionId] = newProcess;
            }
            catch { /* the hook is running either way; losing the liveness record just allows a relaunch */ }

            return true;
        }
    }

    /// <summary>
    /// Whether the hook already living in this session may answer a new request.
    /// </summary>
    /// <remarks>
    /// An elevated request is only answerable by a hook that really runs at High integrity. Handing over an
    /// unelevated one as success is what let a session stay stuck at the normal level for the whole life of
    /// the App: the caller believed it had the hook it asked for, and never asked again (the App-side
    /// fallback in HookLaunchBroker is what puts an unelevated hook in this slot in the first place).
    /// Replacing is refused when the session has no administrator to hand over, so a standard user's working
    /// hook is never disturbed for an elevation nobody can give.
    /// </summary>
    internal static bool KeepLiveHook(bool requestElevation,
        Func<bool> liveHookIsHighIntegrity, Func<bool> sessionCanGrantElevation)
        => !requestElevation
            || liveHookIsHighIntegrity()
            || !sessionCanGrantElevation();

    // The elevated replacement is launched by the caller's next step in this same lock, and a hook that dies
    // here comes back within one round trip of the App's retry loop -- with whatever level the session grants.
    // Two live hooks in one session is not an acceptable middle ground: both would register the global hotkeys.
    private static void StopLiveHook(int sessionId, Process hook)
    {
        Logger.Log($"[UsnService] Replacing session {sessionId}'s unelevated hook (PID {hook.Id}) with an elevated one.");
        try
        {
            hook.Kill();
            if (!hook.WaitForExit(2000))
                Logger.Log($"[UsnService] Hook {hook.Id} did not exit within 2s.", LogLevel.Warn);
        }
        catch (Exception ex)
        {
            Logger.Log($"[UsnService] Could not stop hook {hook.Id}: {ex.Message}", LogLevel.Warn);
        }

        if (_liveHooks.TryRemove(sessionId, out var removed))
            removed.Dispose();
    }

    public static void StopAll()
    {
        lock (_liveHooksGate)
        {
            _stopping = true;
            foreach (var (session, process) in _liveHooks.ToArray())
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        if (!process.WaitForExit(5000)) Logger.Log($"Hook {process.Id} did not exit in time.", LogLevel.Warn);
                    }
                }
                catch (Exception ex) { Logger.Log($"Could not stop hook for session {session}: {ex.Message}", LogLevel.Warn); }
                finally { _liveHooks.TryRemove(session, out _); process.Dispose(); }
            }
        }
    }
}
