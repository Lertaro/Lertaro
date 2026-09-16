namespace Lertaro.App.Services.Pipe;

// Admission control for the App search pipe. Any process that can open the pipe can connect, and every
// accepted connection gets a handler that may run a full search over the App's initialized state, so the
// number of live handlers has to be bounded by something other than the client's goodwill.
//
// Split out of AppSearchPipeService so the admission decision is unit-testable without standing up a real
// pipe (and without a real client, which the ACL would not admit from a test host anyway).
internal sealed class ConnectionSlotGate
{
    private readonly SemaphoreSlim _slots;

    internal ConnectionSlotGate(int maxConcurrent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrent, 1);
        MaxConcurrent = maxConcurrent;
        _slots = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    internal int MaxConcurrent { get; }

    internal int Available => _slots.CurrentCount;

    // Non-blocking on purpose. Queueing an over-limit client would let an unbounded backlog of open
    // connections accumulate while each queued client still holds its pipe handle and socket -- the
    // resource being defended is exactly the one queueing would keep spending. Refusing closes the pipe,
    // which the client can see immediately.
    internal bool TryAcquire() => _slots.Wait(0);

    internal void Release() => _slots.Release();
}
