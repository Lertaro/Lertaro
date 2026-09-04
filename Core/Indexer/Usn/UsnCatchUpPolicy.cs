namespace Lertaro.Core.Indexer.Usn;

internal static class UsnCatchUpPolicy
{
    // A very old cache is cheaper and safer to rebuild than to retain millions of parsed journal
    // records and hold the live index's write lock while applying them as one batch.
    internal const int MaxRecords = 250_000;

    internal static bool CanAccept(int processedRecords) => processedRecords < MaxRecords;
}
