using System.Diagnostics;
using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Hook.Ipc;

// Split out of HookIpcClient to keep that file under the line-count limit. The App never spawns the hook
// process itself anymore -- every launch (elevated or not) is asked of the already-running --service
// instance over IPC (see HookProcessBroker on the Service side), so there's no App-side "runas" UAC
// fallback left to trigger. If the Service is unreachable (not installed, still starting up after
// login/boot, etc.) this just returns null; HookIpcClient's own RunLoop retries with a backoff.
internal sealed class HookLaunchBroker : IDisposable
{
    private readonly SearchService _searchService = new();

    // The elevated hook is asked for first: it is the only one that sees keystrokes typed into elevated
    // windows. The Service grants it only when this session's user is genuinely an administrator and refuses
    // otherwise (SessionProcessLauncher), which is exactly what a standard user's session looks like from
    // here -- and an unelevated hook still registers the global hotkeys. So a decline retries one level down
    // instead of leaving the App with no hook at all. Every other decline (Service still starting, caller not
    // this install's App) repeats on the second request, which then costs one extra round trip and nothing
    // else; the caller's own backoff keeps looping either way.
    public async Task<Process?> LaunchAsync(CancellationToken token)
    {
        var elevated = await RequestAsync(requestElevation: true, token).ConfigureAwait(false);
        if (elevated.Hook != null)
            return elevated.Hook;

        Logger.Log($"[HookIpcClient] Elevated hook launch declined ({elevated.Error}); asking for an unelevated hook.", LogLevel.Debug);
        var plain = await RequestAsync(requestElevation: false, token).ConfigureAwait(false);
        if (plain.Hook == null)
            return null;

        Logger.Log($"[HookIpcClient] Hook launched unelevated (PID {plain.Hook.Id}) -- hotkeys over elevated windows stay out of reach.", LogLevel.Info);
        return plain.Hook;
    }

    // Never throws: RunLoop's backoff only runs on the null result, so an exception escaping here would
    // retry the launch on a hot loop instead.
    private async Task<(Process? Hook, string? Error)> RequestAsync(bool requestElevation, CancellationToken token)
    {
        try
        {
            var (ok, pid, error) = await _searchService.RequestHookLaunchAsync(requestElevation, token).ConfigureAwait(false);
            if (!ok)
            {
                if (!string.IsNullOrEmpty(error))
                {
                    // The hook launch is retried on a backing-off delay by HookIpcClient, so a batch of these
                    // is expected while the service is still coming up on boot; downgrade to Debug
                    // inside the cold-start window and keep real failures afterwards visible.
                    var level = ServicePipeReadinessGate.Instance.IsColdStart(Environment.TickCount64)
                        ? LogLevel.Debug
                        : LogLevel.Warn;
                    Logger.Log($"[HookIpcClient] Service declined hook launch: {error}", level);
                }
                return (null, error);
            }

            return (Process.GetProcessById(pid), error);
        }
        catch (Exception ex)
        {
            Logger.Log($"[HookIpcClient] Service hook-launch request failed: {ex.Message}", LogLevel.Warn);
            return (null, ex.Message);
        }
    }

    public void Dispose() => _searchService.Dispose();
}
