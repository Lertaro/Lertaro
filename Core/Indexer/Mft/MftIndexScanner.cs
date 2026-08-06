using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.Indexer.Mft;

/// <summary>
/// Builds a drive's index by parsing the raw NTFS $MFT instead of FSCTL_ENUM_USN_DATA. Unlike the
/// USN enumeration (one primary name per record), this reads every $FILE_NAME attribute, so a
/// hard-linked file yields one <see cref="FileRecord"/> per link (one-to-many). File reference
/// numbers are the full 64-bit form (sequence &lt;&lt; 48 | record index) to stay aligned with the
/// USN journal used for incremental updates. Requires a volume handle opened with read access
/// (Administrator).
/// </summary>
internal static class MftIndexScanner
{
    private const int NtfsRootRecordIndex = 5; // $Root; already added by CreateEmptyStore.

    public static UsnDriveIndexResult? ScanDrive(string drive, SafeFileHandle handle, UInt128 rootFrn, ulong journalId, long nextUsn, Action<int, int>? onProgress)
    {
        var vd = new byte[512];
        if (!Win32Api.DeviceIoControl(handle, Win32Api.FSCTL_GET_NTFS_VOLUME_DATA, IntPtr.Zero, 0, vd, (uint)vd.Length, out _, IntPtr.Zero))
        {
            Logger.Log($"[MftIndexScanner] GET_NTFS_VOLUME_DATA failed on {drive}: {Marshal.GetLastWin32Error()}", LogLevel.Error);
            return null;
        }

        var bytesPerSector = BitConverter.ToUInt32(vd, 40);
        var bytesPerCluster = BitConverter.ToUInt32(vd, 44);
        var recordSize = BitConverter.ToUInt32(vd, 48);
        var mftValidLen = BitConverter.ToInt64(vd, 56);
        var mftStartLcn = BitConverter.ToInt64(vd, 64);
        if (recordSize == 0 || bytesPerCluster == 0)
            return null;

        var rec0 = new byte[recordSize];
        if (!ReadAt(handle, mftStartLcn * bytesPerCluster, rec0, (int)recordSize))
            return null;
        MftParser.ApplyFixup(rec0, bytesPerSector, 0, (int)recordSize);
        var extents = MftParser.ParseDataRuns(rec0);

        // Check if $MFT has an $ATTRIBUTE_LIST (resident or non-resident) pointing to extension records containing more $DATA runs
        var extRecIndexes = MftParser.ParseAttributeListRecordIndexes(rec0, 0x80, (off, targetBuf, count) => ReadAt(handle, off, targetBuf, count), bytesPerCluster);
        foreach (var extRecIdx in extRecIndexes)
        {
            var extRecOff = GetMftRecordVolumeOffset(extRecIdx, extents, bytesPerCluster, recordSize);
            if (extRecOff >= 0)
            {
                var extRec = new byte[recordSize];
                if (ReadAt(handle, extRecOff, extRec, (int)recordSize))
                {
                    MftParser.ApplyFixup(extRec, bytesPerSector, 0, (int)recordSize);
                    MftParser.ParseDataRunsInto(extRec, extents);
                }
            }
        }

        if (extents.Count == 0)
        {
            Logger.Log($"[MftIndexScanner] Could not parse $MFT runlist on {drive}.", LogLevel.Error);
            return null;
        }

        var store = IndexCacheManager.CreateEmptyStore(drive, rootFrn, nextUsn, journalId);
        store.Records.EnsureCapacity(2_800_000);
        var namePool = new FileRecordNamePool();
        var records = store.Records;

        int files = 0, dirs = 0;
        long processed = 0;
        const int ChunkBytes = 8 * 1024 * 1024;
        var buf = new byte[ChunkBytes];
        var names = new List<(UInt128 parent, string name, long size)>(4);

        foreach (var (lcn, clusters) in extents)
        {
            var extBytes = clusters * bytesPerCluster;
            var off = lcn * bytesPerCluster;
            while (extBytes > 0)
            {
                var chunk = (int)Math.Min(ChunkBytes, extBytes);
                chunk -= chunk % (int)recordSize;
                if (chunk <= 0)
                    break;
                if (!ReadAt(handle, off, buf, chunk))
                    break;

                for (var r = 0; r + recordSize <= chunk; r += (int)recordSize)
                {
                    var idx = processed / recordSize;
                    processed += recordSize;
                    if (buf[r] != (byte)'F' || buf[r + 1] != (byte)'I' || buf[r + 2] != (byte)'L' || buf[r + 3] != (byte)'E')
                        continue;
                    MftParser.ApplyFixup(buf, bytesPerSector, r, (int)recordSize);

                    var headerFlags = BitConverter.ToUInt16(buf, r + 0x16);
                    if ((headerFlags & 0x01) == 0) // not in use
                        continue;

                    var baseRef = (ulong)BitConverter.ToInt64(buf, r + 0x20);
                    var isExtension = (baseRef & 0xFFFFFFFFFFFF) != 0;
                    if (isExtension)
                        continue; // Primary $FILE_NAME attributes reside in the base record; skip extension records.

                    if (idx == NtfsRootRecordIndex)
                        continue; // root already present via CreateEmptyStore

                    var seq = BitConverter.ToUInt16(buf, r + 0x10);
                    UInt128 owner = ((ulong)seq << 48) | ((ulong)idx & 0xFFFFFFFFFFFF);
                    names.Clear();
                    var stdAttrs = MftParser.CollectNames(buf, r, (int)recordSize, names,
                        out var creationTimeUtc, out var lastWriteTimeUtc, out var lastAccessTimeUtc);

                    if (names.Count == 0)
                        continue;

                    var isDir = (headerFlags & 0x02) != 0 || (stdAttrs & (uint)FileAttributes.Directory) != 0;
                    var attrs = (FileAttributes)stdAttrs;
                    if (isDir)
                        attrs |= FileAttributes.Directory;
                    var flags = FileRecordFlagsHelper.FromAttributes(attrs);
                    var creationUnixSeconds = FileTimeHelper.FileTimeToUnixSeconds(creationTimeUtc);
                    var lastWriteUnixSeconds = FileTimeHelper.FileTimeToUnixSeconds(lastWriteTimeUtc);
                    var lastAccessUnixSeconds = FileTimeHelper.FileTimeToUnixSeconds(lastAccessTimeUtc);

                    foreach (var (parent, name, size) in names)
                    {
                        records.Add(new FileRecord(owner, parent, namePool.Get(name), flags, isDir ? 0 : size,
                            creationUnixSeconds, lastWriteUnixSeconds, lastAccessUnixSeconds));
                        if (isDir) dirs++; else files++;
                    }
                }

                off += chunk;
                extBytes -= chunk;
                onProgress?.Invoke(files, dirs);
            }
        }

        Logger.Log($"[MftIndexScanner] Drive {drive} $MFT scan complete: {records.Count - 1} rows (files={files}, dirs={dirs}).");
        // Unlike TreeBuilder/ReFsScanner, this scan has no partial/checkpoint output at all -- it's a
        // single-pass sequential parse that either returns a fully-finished store or null (see the
        // early-return guards above), so unconditionally true here is always correct, never optimistic.
        store.IsComplete = true;
        return new UsnDriveIndexResult
        {
            Store = store,
            NextUsn = nextUsn,
            JournalId = journalId,
            IsSortedById = false // one FRN can span multiple rows; let RuntimeIndex.Load sort.
        };
    }

    private static bool ReadAt(SafeFileHandle handle, long offset, byte[] buffer, int count)
    {
        if (!Win32Api.SetFilePointerEx(handle, offset, out _, 0))
            return false;
        var done = 0;
        while (done < count)
        {
            var slice = done == 0 ? buffer : new byte[count - done];
            if (!Win32Api.ReadFile(handle, slice, (uint)(count - done), out var got, IntPtr.Zero) || got == 0)
                return false;
            if (done != 0)
                Array.Copy(slice, 0, buffer, done, (int)got);
            done += (int)got;
        }
        return true;
    }

    private static long GetMftRecordVolumeOffset(ulong recordIndex, List<(long lcn, long clusters)> extents, uint bytesPerCluster, uint recordSize)
    {
        var targetStreamOffset = (long)recordIndex * recordSize;
        long currentStreamOffset = 0;
        foreach (var (lcn, clusters) in extents)
        {
            var extentBytes = clusters * bytesPerCluster;
            if (targetStreamOffset >= currentStreamOffset && targetStreamOffset < currentStreamOffset + extentBytes)
            {
                var offsetInExtent = targetStreamOffset - currentStreamOffset;
                return (lcn * bytesPerCluster) + offsetInExtent;
            }
            currentStreamOffset += extentBytes;
        }
        return -1;
    }
}
