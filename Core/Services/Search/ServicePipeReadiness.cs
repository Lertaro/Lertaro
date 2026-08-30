using System.Threading;

namespace Lertaro.Core.Services.Search;

/// <summary>
/// Tracks whether the service pipe has ever answered, so cold-start connect failures can be
/// logged at Debug instead of Warn: the App boots faster than the service finishes initializing,
/// so the first seconds of pipe requests (hook launch, directory enumeration for content indexing
/// and quick panels) fail with connect timeouts even though everything is healthy. Split out as
/// an instance class so the window decision is unit-testable; <see cref="Gate"/> holds the
/// process-wide instance.
/// </summary>
internal sealed class ServicePipeReadiness
{
    private readonly long _coldStartWindowMs;
    private readonly long _startedAtTickCount;
    private int _everConnected;

    internal ServicePipeReadiness(long coldStartWindowMs, long startedAtTickCount)
    {
        _coldStartWindowMs = coldStartWindowMs;
        _startedAtTickCount = startedAtTickCount;
    }

    /// <summary>Records that the pipe answered at least once; cold-start treatment ends.</summary>
    internal void MarkConnected() => Interlocked.Exchange(ref _everConnected, 1);

    /// <summary>
    /// True while the service has never answered a request AND the process is still inside the
    /// cold-start window. Past the window an unreachable pipe is a real fault again (the service
    /// is down or not installed at all) and stays visible at Warn.
    /// </summary>
    internal bool IsColdStart(long nowTickCount) =>
        Volatile.Read(ref _everConnected) == 0 && nowTickCount - _startedAtTickCount < _coldStartWindowMs;
}

internal static class ServicePipeReadinessGate
{
    // Two minutes of "the service is still coming up", well past its measured ~20s startup, but
    // bounded so a service that never comes up becomes visible again instead of logging quietly.
    internal static readonly ServicePipeReadiness Instance =
        new(coldStartWindowMs: 120_000, startedAtTickCount: Environment.TickCount64);
}
