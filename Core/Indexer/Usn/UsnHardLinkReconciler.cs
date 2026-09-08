using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Delta;

namespace Lertaro.Core.Indexer.Usn;

internal static class UsnHardLinkReconciler
{
    internal static void Apply(LiveIndex live, List<ParsedUsnRecord> records)
    {
        var isNtfs = live.Read((snapshot, _) => snapshot.FileSystemType.Equals("NTFS", StringComparison.OrdinalIgnoreCase));
        foreach (var record in records)
        {
            var path = live.Read((snapshot, delta) => delta.TryGetPathForFrn(record.ParentFileReferenceNumber, out var parent)
                ? Path.Combine(parent, record.FileName) : null);
            if (isNtfs && path != null && UsnMetadataReader.IsNtfsInternalPath(path))
                continue;

            var result = path == null
                ? new UsnMetadataReader.MetadataReadResult(UsnMetadataReader.MetadataReadStatus.Missing, default, 0)
                : UsnMetadataReader.TryRead(path, record.FileReferenceNumber);
            if (result.Status == UsnMetadataReader.MetadataReadStatus.Unavailable)
                continue;
            // HARD_LINK_CHANGE has no add/remove polarity. Observe this exact name instead of toggling:
            // duplicate CLOSE records and replay after an interrupted batch must be idempotent.
            live.Mutate((snapshot, delta) => DeltaLinkOps.SetLinkPresence(delta, record.FileReferenceNumber,
                record.ParentFileReferenceNumber, record.FileName, result.Value.Flags,
                result.Status == UsnMetadataReader.MetadataReadStatus.Present));
        }
    }
}
