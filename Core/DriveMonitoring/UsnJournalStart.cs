namespace Lertaro.Core.DriveMonitoring;

// Where a freshly started monitor may begin reading.
//
// A full rebuild takes 90+ seconds on a large system drive while a 32 MB journal wraps in about as many
// minutes of ordinary churn, so the position captured before the scan is routinely already deleted by
// the time its monitor starts. The read then fails with 1181, the monitor asks for a re-index, and the
// rebuild that follows hands the next monitor the very same unusable position -- a loop that never
// converges and costs a full $MFT scan plus a multi-hundred-MB snapshot rewrite every turn.
//
// Resuming from the journal's tail instead of asking for that rebuild keeps the index live at the price
// of the changes between the scan and the monitor's start; the next rebuild reconciles those.
internal readonly record struct UsnStartDecision(long StartUsn, bool SkippedGap);

internal static class UsnJournalStart
{
    internal static UsnStartDecision Resolve(long recordedUsn, ulong recordedJournalId, UsnJournalInfo journal)
    {
        // A different journal ID means the recorded position names a journal that no longer exists, which
        // clamping cannot repair: UsnMonitor's own ID check owns that case and asks for the re-index such
        // a stamp genuinely needs.
        if (journal.JournalId != recordedJournalId)
            return new(recordedUsn, false);

        // Ahead of NextUsn is only a raced snapshot of a wrapping journal; behind FirstUsn is the ordinary
        // case. Both mean "not readable", so both restart at the tail.
        if (recordedUsn > journal.NextUsn || recordedUsn < journal.FirstUsn)
            return new(journal.NextUsn, true);

        return new(recordedUsn, false);
    }
}
