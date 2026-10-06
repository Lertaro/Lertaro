using System.Collections.Concurrent;
using System.Diagnostics;

namespace Lertaro.Core.Services.HookLaunch;

// Runs inside the SYSTEM-privileged --service process. Every hook launch -- elevated or not -- goes
// through here rather than the App spawning its own child process, so the App never has to hold a
// runas/UAC fallback of its own. The actual cross-session launch is SessionProcessLauncher's; this type
// adds the one thing specific to hooks: a per-session record of the live process, so repeated requests
// don't spawn duplicates.
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
                try
                {
                    if (!existing.HasExited)
                    {
                        pid = existing.Id;
                        return true;
                    }
                }
                catch { /* process object stale; fall through and relaunch */ }
                if (_liveHooks.TryRemove(sessionId, out var removed))
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
