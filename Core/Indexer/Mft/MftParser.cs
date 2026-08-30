using System.Text;

namespace Lertaro.Core.Indexer.Mft;

/// <summary>Low-level NTFS $MFT structure parsing: fixup, $DATA run lists, and attribute walking.</summary>
internal static class MftParser
{
    /// <summary>Applies the update-sequence-array fixup to a raw FILE record in place.</summary>
    internal static void ApplyFixup(byte[] buf, uint bytesPerSector, int recOff, int recLen)
    {
        int usaOff = BitConverter.ToUInt16(buf, recOff + 4);
        int usaCount = BitConverter.ToUInt16(buf, recOff + 6);
        for (var i = 1; i < usaCount; i++)
        {
            var secEnd = recOff + i * (int)bytesPerSector - 2;
            var usaEntry = recOff + usaOff + i * 2;
            if (secEnd + 2 > recOff + recLen || usaEntry + 2 > buf.Length)
                break;
            buf[secEnd] = buf[usaEntry];
            buf[secEnd + 1] = buf[usaEntry + 1];
        }
    }

    /// <summary>Parses the $MFT's own non-resident $DATA run list into (lcn, clusterCount) extents.</summary>
    internal static List<(long lcn, long clusters)> ParseDataRuns(byte[] rec) => MftDataRunParser.ParseDataRuns(rec);

    internal static void ParseDataRunsInto(byte[] rec, List<(long lcn, long clusters)> extents) =>
        MftDataRunParser.ParseDataRunsInto(rec, extents);

    /// <summary>Parses resident and non-resident $ATTRIBUTE_LIST (0x20) entries to find record indexes of extension records holding <paramref name="targetAttrType"/>.</summary>
    internal static List<ulong> ParseAttributeListRecordIndexes(byte[] rec, uint targetAttrType, Func<long, byte[], int, bool>? readAt = null, uint bytesPerCluster = 0)
    {
        var records = new List<ulong>();
        int a = BitConverter.ToUInt16(rec, 0x14);
        while (a + 8 <= rec.Length)
        {
            var type = BitConverter.ToUInt32(rec, a);
            if (type == 0xFFFFFFFF)
                break;
            var len = BitConverter.ToUInt32(rec, a + 4);
            if (len < 16 || a + len > rec.Length)
                break;
            if (type == 0x20) // $ATTRIBUTE_LIST
            {
                var resident = rec[a + 8] == 0;
                if (resident)
                {
                    var vo = BitConverter.ToUInt16(rec, a + 0x14);
                    var vl = BitConverter.ToUInt32(rec, a + 0x10);
                    var p = a + vo;
                    var end = p + (int)vl;
                    if (end <= rec.Length)
                    {
                        ParseAttributeListEntries(rec.AsSpan(p, (int)vl), targetAttrType, records);
                    }
                }
                else if (readAt != null && bytesPerCluster > 0 && a + 0x38 <= rec.Length)
                {
                    var realSize = BitConverter.ToInt64(rec, a + 0x30);
                    if (realSize > 0 && realSize <= 16 * 1024 * 1024) // sanity cap 16MB for attribute list
                    {
                        var attrExtents = new List<(long lcn, long clusters)>();
                        ParseDataRunsFromAttribute(rec, a, attrExtents);
                        var attrBuf = new byte[realSize];
                        long readOffset = 0;
                        var success = true;
                        foreach (var (lcn, clusters) in attrExtents)
                        {
                            var bytesToRead = (int)Math.Min(clusters * bytesPerCluster, realSize - readOffset);
                            if (bytesToRead <= 0) break;
                            var alignedReadBytes = (int)(clusters * bytesPerCluster);
                            var tempBuf = new byte[alignedReadBytes];
                            if (!readAt(lcn * bytesPerCluster, tempBuf, alignedReadBytes))
                            {
                                success = false;
                                break;
                            }
                            Buffer.BlockCopy(tempBuf, 0, attrBuf, (int)readOffset, bytesToRead);
                            readOffset += bytesToRead;
                        }
                        if (success && readOffset > 0)
                        {
                            ParseAttributeListEntries(attrBuf.AsSpan(0, (int)readOffset), targetAttrType, records);
                        }
                    }
                }
            }
            a += (int)len;
        }
        return records;
    }

    internal static void ParseDataRunsFromAttribute(byte[] rec, int attrOffset, List<(long lcn, long clusters)> extents) =>
        MftDataRunParser.ParseDataRunsFromAttribute(rec, attrOffset, extents);

    internal static bool HasAttribute(byte[] buf, int recOff, int recLen, uint targetAttrType)
    {
        int a = BitConverter.ToUInt16(buf, recOff + 0x14);
        while (a + 8 <= recLen)
        {
            var type = BitConverter.ToUInt32(buf, recOff + a);
            if (type == 0xFFFFFFFF)
                return false;
            var len = BitConverter.ToUInt32(buf, recOff + a + 4);
            if (len < 16 || a + len > recLen)
                return false;
            if (type == targetAttrType)
                return true;
            a += (int)len;
        }
        return false;
    }

    private static void ParseAttributeListEntries(ReadOnlySpan<byte> buffer, uint targetAttrType, List<ulong> records)
    {
        var p = 0;
        while (p + 0x18 <= buffer.Length)
        {
            var entryType = BitConverter.ToUInt32(buffer.Slice(p, 4));
            var entryLen = BitConverter.ToUInt16(buffer.Slice(p + 0x04, 2));
            if (entryLen < 0x18 || p + entryLen > buffer.Length)
                break;
            if (entryType == targetAttrType)
            {
                var mftRef = BitConverter.ToUInt64(buffer.Slice(p + 0x10, 8));
                var recIdx = mftRef & 0xFFFFFFFFFFFF;
                if (recIdx > 0 && !records.Contains(recIdx))
                    records.Add(recIdx);
            }
            p += entryLen;
        }
    }


    /// <summary>
    /// Walks a FILE record's attributes: collects every resident $FILE_NAME (excluding DOS-only 8.3
    /// short names) as (parentReference, name, realSize) into <paramref name="names"/>, and returns the
    /// $STANDARD_INFORMATION file attributes (hidden/system/etc) plus its Creation/LastWrite/LastAccess
    /// FILETIMEs (shared by every hard-linked name, unlike size which $FILE_NAME tracks per link).
    /// </summary>
    internal static uint CollectNames(byte[] buf, int recOff, int recLen, List<(UInt128 parent, string name, long size)> names,
        out long creationTimeUtc, out long lastWriteTimeUtc, out long lastAccessTimeUtc)
    {
        uint stdAttrs = 0;
        creationTimeUtc = 0;
        lastWriteTimeUtc = 0;
        lastAccessTimeUtc = 0;
        // $FILE_NAME's own "real size" (read below, per name) is $DATA's real size DUPLICATED into the
        // directory-entry-like $FILE_NAME record for fast listing -- NTFS only refreshes that copy on
        // rename/move/link, so it can sit stale (often 0) for a file that was written once and never
        // touched again. $DATA's own real-size field (read here from the same already-loaded record,
        // no extra I/O) is the one NTFS keeps authoritative, so it wins whenever present.
        long? dataSize = null;
        (UInt128 parent, string name, long size)? dosFallbackName = null;
        int a = BitConverter.ToUInt16(buf, recOff + 0x14);
        while (a + 8 <= recLen)
        {
            var type = BitConverter.ToUInt32(buf, recOff + a);
            if (type == 0xFFFFFFFF)
                break;
            var len = BitConverter.ToUInt32(buf, recOff + a + 4);
            if (len < 16 || a + len > recLen)
                break;
            var resident = buf[recOff + a + 8] == 0;
            if (type == 0x10 && resident) // $STANDARD_INFORMATION
            {
                var vo = BitConverter.ToUInt16(buf, recOff + a + 0x14);
                if (a + vo + 0x24 <= recLen)
                {
                    creationTimeUtc = BitConverter.ToInt64(buf, recOff + a + vo + 0x00);
                    lastWriteTimeUtc = BitConverter.ToInt64(buf, recOff + a + vo + 0x08);
                    lastAccessTimeUtc = BitConverter.ToInt64(buf, recOff + a + vo + 0x18);
                    stdAttrs = BitConverter.ToUInt32(buf, recOff + a + vo + 0x20);
                }
            }
            else if (type == 0x30 && resident) // $FILE_NAME
            {
                var vo = BitConverter.ToUInt16(buf, recOff + a + 0x14);
                var vp = recOff + a + vo;
                if (vp + 0x42 <= recOff + recLen)
                {
                    var fnAttrs = BitConverter.ToUInt32(buf, vp + 0x38);
                    if ((fnAttrs & (uint)FileAttributes.Directory) != 0)
                        stdAttrs |= (uint)FileAttributes.Directory;

                    var ns = buf[vp + 0x41]; // 0=POSIX 1=Win32 2=DOS 3=Win32&DOS
                    UInt128 parent = (ulong)BitConverter.ToInt64(buf, vp);
                    var size = BitConverter.ToInt64(buf, vp + 0x30); // real (logical) size -- may be stale, see dataSize above
                    int nameLen = buf[vp + 0x40];
                    if (vp + 0x42 + nameLen * 2 <= recOff + recLen)
                    {
                        var parsedName = Encoding.Unicode.GetString(buf, vp + 0x42, nameLen * 2);
                        if (ns != 2)
                        {
                            names.Add((parent, parsedName, size));
                        }
                        else
                        {
                            dosFallbackName ??= (parent, parsedName, size);
                        }
                    }
                }
            }
            else if (type == 0x80 && buf[recOff + a + 0x09] == 0) // $DATA, unnamed stream only (skip alternate data streams)
            {
                if (resident)
                {
                    // Resident value length IS the file's content length -- no separate size field.
                    if (a + 0x14 <= recLen)
                        dataSize = BitConverter.ToUInt32(buf, recOff + a + 0x10);
                }
                else if (a + 0x38 <= recLen)
                {
                    dataSize = BitConverter.ToInt64(buf, recOff + a + 0x30); // real (logical) size, always current
                }
            }
            a += (int)len;
        }

        if (names.Count == 0 && dosFallbackName.HasValue)
        {
            names.Add(dosFallbackName.Value);
        }

        if (dataSize.HasValue)
        {
            for (var i = 0; i < names.Count; i++)
            {
                var (parent, name, _) = names[i];
                names[i] = (parent, name, dataSize.Value);
            }
        }

        return stdAttrs;
    }

}
