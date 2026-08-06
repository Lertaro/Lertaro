namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

// Thread-safe "should a checkpoint fire now" gate, shared by TreeBuilder (network/WSL/folder-index drives)
// and ReFsScanner's own mid-walk checkpointing (Core/Indexer/Usn/Journal/ReFsScannerCheckpointExtensions.cs)
// -- both walk in parallel across multiple worker tasks and both re-serialize their FULL accumulated state
// on every checkpoint (not a delta), so a flat interval would make total write volume grow with the SQUARE
// of the walk size: checkpoint k always costs O(k), and there are O(n/batchSize) of them. Doubling the gap
// after each checkpoint (capped at maxBatchSize) keeps total write volume within roughly 6-10x the final
// size regardless of how large the walk is, instead of many hundreds of times over on a multi-million-record
// one -- e.g. ~2.9GB total written for a 5M-record rebuild whose final snapshot is ~0.5GB. Chosen as a
// balance against the OTHER cost this trades against: how much of the walk has to be redone if the process
// is interrupted right after a checkpoint (worst case is one full cap's worth of items).
internal sealed class DoublingCheckpointGate
{
    private readonly int _maxBatchSize;
    private int _batchSize;
    private int _countSinceCheckpoint;
    private int _checkpointInFlight;

    public DoublingCheckpointGate(int initialBatchSize, int maxBatchSize)
    {
        _batchSize = initialBatchSize;
        _maxBatchSize = maxBatchSize;
    }

    public int BatchSize => Volatile.Read(ref _batchSize);

    // Returns true at most once per threshold crossing, even under many concurrent callers -- the caller
    // MUST call Completed() afterward (whether or not the checkpoint itself succeeded) before another one
    // can ever fire; a TryEnter() that returns true without a matching Completed() wedges this gate shut.
    public bool TryEnter()
    {
        var threshold = Volatile.Read(ref _batchSize);
        var count = Interlocked.Increment(ref _countSinceCheckpoint);
        if (count < threshold)
            return false;

        // Guards against multiple threads crossing the threshold at once: only the one whose reset
        // actually finds a nonzero counter proceeds, so exactly one checkpoint fires per threshold crossing.
        if (Interlocked.Exchange(ref _countSinceCheckpoint, 0) == 0)
            return false;

        // A caller with no I/O throttling of its own (e.g. a mostly-reused resume) can cross the threshold
        // again before the PREVIOUS checkpoint's own work (disk write, etc.) finishes. Skipping (not
        // blocking) is safe: nothing is lost, whatever would've gone into this checkpoint just rides along
        // in the next one that actually gets to run.
        return Interlocked.CompareExchange(ref _checkpointInFlight, 1, 0) == 0;
    }

    // Doubles the gap before the next checkpoint (capped) and releases the in-flight guard.
    public void Completed()
    {
        var threshold = Volatile.Read(ref _batchSize);
        Volatile.Write(ref _batchSize, Math.Min(threshold * 2, _maxBatchSize));
        Interlocked.Exchange(ref _checkpointInFlight, 0);
    }
}
