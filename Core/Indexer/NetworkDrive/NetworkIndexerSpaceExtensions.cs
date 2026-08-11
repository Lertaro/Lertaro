using Lertaro.Core.IndexV2.Space;

namespace Lertaro.Core.Indexer.NetworkDrive;

/// <summary>Queries network/WSL/folder indexes already loaded in the App process.</summary>
internal static class NetworkIndexerSpaceExtensions
{
    public static List<SpaceIndexEntry> GetSpaceEntries(this NetworkIndexer indexer, string? directory)
    {
        NetworkIndex[] indexes;
        lock (indexer.Gate)
            indexes = indexer._indexes.Values.ToArray();

        var result = new List<SpaceIndexEntry>();
        foreach (var index in indexes)
        {
            var query = index.GetSpaceEntries(directory);
            if (!query.Found)
                continue;
            result.AddRange(query.Entries);
            if (!string.IsNullOrWhiteSpace(directory))
                break;
        }
        return result;
    }
}
