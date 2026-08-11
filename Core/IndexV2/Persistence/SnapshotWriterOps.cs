namespace Lertaro.Core.IndexV2.Persistence;

/// <summary>
/// Helper operations for SnapshotWriter to keep file line count strictly under 300 lines.
/// Split out purely to comply with the repo's per-file line limit; these operations act on snapshot streams and ID arrays.
/// </summary>
internal static class SnapshotWriterOps
{
    internal static long[] BuildRecursiveSizes(UInt128[] ids, ushort[] flags, long[] sizes, int[] parentIndexes, UInt128 rootId)
    {
        var totals = new long[ids.Length];
        var remainingChildren = new int[ids.Length];
        var root = FirstRowForId(ids, rootId);

        for (var row = 0; row < ids.Length; row++)
        {
            var isDirectory = (flags[row] & (ushort)FileRecordFlags.Directory) != 0;
            // Hard-link rows are adjacent because the snapshot is ID-sorted. Attribute the bytes to
            // one indexed name so links cannot inflate the volume total.
            if (!isDirectory && (row == 0 || ids[row - 1] != ids[row]))
                totals[row] = Math.Max(0, sizes[row]);

            var parent = EffectiveParent(row, parentIndexes, root);
            if (parent >= 0)
                remainingChildren[parent]++;
        }

        var queue = new Queue<int>();
        for (var row = 0; row < ids.Length; row++)
            if (remainingChildren[row] == 0)
                queue.Enqueue(row);

        while (queue.Count > 0)
        {
            var row = queue.Dequeue();
            var parent = EffectiveParent(row, parentIndexes, root);
            if (parent < 0)
                continue;
            totals[parent] = SaturatingAdd(totals[parent], totals[row]);
            if (--remainingChildren[parent] == 0)
                queue.Enqueue(parent);
        }

        return totals;
    }

    private static int EffectiveParent(int row, int[] parentIndexes, int root)
    {
        var parent = parentIndexes[row];
        if (parent < 0 && row != root)
            parent = root;
        return (uint)parent < (uint)parentIndexes.Length && parent != row ? parent : -1;
    }

    private static long SaturatingAdd(long left, long right)
        => left > long.MaxValue - right ? long.MaxValue : left + right;

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
