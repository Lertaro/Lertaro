namespace Lertaro.Core.IndexV2.Persistence;

/// <summary>
/// Helper operations for SnapshotWriter to keep file line count strictly under 300 lines.
/// Split out purely to comply with the repo's per-file line limit; these operations act on snapshot streams and ID arrays.
/// </summary>
internal static class SnapshotWriterOps
{
    // First (lowest) row holding this id, or -1 -- hard-link duplicates sit adjacent after the sort.
    internal static int FirstRowForId(UInt128[] ids, UInt128 id)
    {
        int low = 0, high = ids.Length - 1, found = -1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            if (ids[mid] >= id)
            {
                if (ids[mid] == id)
                    found = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }
        return found;
    }

    // Fallback lookup by 48-bit MFT Record Index when 64-bit FRN sequence numbers mismatch.
    internal static int ResolveParentIndexWithRecordIndexFallback(UInt128[] ids, UInt128 parentId, ref Dictionary<ulong, int>? recordIndexMap)
    {
        var idx = FirstRowForId(ids, parentId);
        if (idx >= 0)
            return idx;

        if (recordIndexMap == null)
        {
            recordIndexMap = new Dictionary<ulong, int>(ids.Length);
            for (var i = 0; i < ids.Length; i++)
            {
                var recordIndex = (ulong)ids[i] & 0xFFFFFFFFFFFF;
                recordIndexMap.TryAdd(recordIndex, i);
            }
        }

        var parentRecordIndex = (ulong)parentId & 0xFFFFFFFFFFFF;
        return recordIndexMap.TryGetValue(parentRecordIndex, out var fallbackIdx) ? fallbackIdx : -1;
    }

    internal static void WriteSection(FileStream stream, long[] offsets, SnapshotSection section, ReadOnlySpan<byte> bytes)
    {
        stream.Position = offsets[(int)section];
        stream.Write(bytes);
    }

    internal static void TryDelete(string path)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            catch when (attempt < maxAttempts)
            {
                Thread.Sleep(25 * attempt);
            }
            catch
            {
            }
        }
    }
}
