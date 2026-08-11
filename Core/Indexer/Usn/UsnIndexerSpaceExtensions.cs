using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Space;

namespace Lertaro.Core.Indexer.Usn;

/// <summary>Queries the local drives already loaded by the service; no cache files are opened.</summary>
internal static class UsnIndexerSpaceExtensions
{
    public static List<SpaceIndexEntry> GetSpaceEntries(this UsnIndexer indexer, string? directory)
    {
        LiveIndex[] indexes;
        lock (indexer.LockObj)
            indexes = indexer._recordIndexes.Values.ToArray();

        var result = new List<SpaceIndexEntry>();
        foreach (var live in indexes)
        {
            try
            {
                var query = LiveSpaceQuery.GetEntries(live, directory);
                if (!query.Found)
                    continue;
                result.AddRange(query.Entries);
                if (!string.IsNullOrWhiteSpace(directory))
                    break;
            }
            catch (ObjectDisposedException)
            {
                // A rebuild replaced this drive after the structural snapshot above.
            }
        }
        return result;
    }
}
