using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Abstractions;

using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.IndexV2.Search;

// Converts a ranked FzfRank entry into a public SearchResult. entryIndex < Snapshot.Count is a base
// row (possibly overridden); entryIndex >= Snapshot.Count addresses delta.Added[entryIndex - Count].
internal static class ResultBuilder
{
    public static SearchResult ToResult(Snapshot snapshot, DeltaOverlay delta, FzfRank rank,
        Func<int, long>? getDirectorySize = null)
    {
        var entryIndex = rank.EntryIndex;
        if (entryIndex >= snapshot.Count)
        {
            var record = delta.Added[entryIndex - snapshot.Count];
            var isDirectory = (record.Flags & (ushort)FileRecordFlags.Directory) != 0;
            var resultSize = isDirectory && getDirectorySize != null
                ? getDirectorySize(entryIndex)
                : record.Size;
            return new SearchResult
            {
                Name = record.Name,
                Path = delta.GetFullPath(record),
                IsDir = isDirectory,
                Drive = snapshot.SourceKey,
                Attributes = FileRecordFlagsHelper.ToAttributes((FileRecordFlags)record.Flags),
                RankSortKey = rank.SortKey,
                Metadata = ToMetadata(resultSize, record.Creation, record.LastWrite, record.LastAccess),
            };
        }

        var (size, creation, lastWrite, lastAccess) = delta.MetadataOf(entryIndex);
        var flags = delta.BaseOverrides.TryGetValue(entryIndex, out var o) ? (FileRecordFlags)o.Flags : (FileRecordFlags)snapshot.Flags[entryIndex];
        var isDir = (flags & FileRecordFlags.Directory) != 0;
        if (isDir && getDirectorySize != null)
            size = getDirectorySize(entryIndex);
        return new SearchResult
        {
            Name = delta.NameOf(entryIndex),
            Path = delta.GetFullPath(entryIndex),
            IsDir = isDir,
            Drive = snapshot.SourceKey,
            Attributes = FileRecordFlagsHelper.ToAttributes(flags),
            RankSortKey = rank.SortKey,
            Metadata = ToMetadata(size, creation, lastWrite, lastAccess),
        };
    }

    private static FileMetadata ToMetadata(long size, uint creation, uint lastWrite, uint lastAccess) => new(
        size,
        FileTimeHelper.FromUnixSeconds(creation).ToLocalTime(),
        FileTimeHelper.FromUnixSeconds(lastWrite).ToLocalTime(),
        FileTimeHelper.FromUnixSeconds(lastAccess).ToLocalTime());
}
