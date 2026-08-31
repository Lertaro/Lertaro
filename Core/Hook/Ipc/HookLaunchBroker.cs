using System.Diagnostics;
using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Hook.Ipc;

// Split out of HookIpcClient to keep that file under the line-count limit. The App never spawns the hook
// process itself anymore -- every launch (elevated or not) is asked of the already-running --service
// instance over IPC (see HookProcessBroker on the Service side), so there's no App-side "runas" UAC
// fallback left to trigger. If the Service is unreachable (not installed, still starting up after
// login/boot, etc.) this just returns null; HookIpcClient's own RunLoop already retries every 5s.
internal sealed class HookLaunchBroker : IDisposable
{
    private readonly SearchService _searchService = new();

    public async Task<Process?> LaunchAsync(bool requestElevation, CancellationToken token)
    {
        try
        {
            var (ok, pid, error) = await _searchService.RequestHookLaunchAsync(requestElevation, token).ConfigureAwait(false);
            if (!ok)
            {
                if (!string.IsNullOrEmpty(error))
                {
                    // The hook launch is retried every 5s by HookIpcClient, so a batch of these is
                    // expected while the service is still coming up on boot; downgrade to Debug
                    // inside the cold-start window and keep real failures afterwards visible.
                    var level = ServicePipeReadinessGate.Instance.IsColdStart(Environment.TickCount64)
                        ? LogLevel.Debug
                        : LogLevel.Warn;
                    Logger.Log($"[HookIpcClient] Service declined hook launch: {error}", level);
                }
                return null;
            }

            return Process.GetProcessById(pid);
        }
        catch (Exception ex)
        {
            Logger.Log($"[HookIpcClient] Service hook-launch request failed: {ex.Message}", LogLevel.Warn);
            return null;
        }
    }

    public void Dispose() => _searchService.Dispose();
}
