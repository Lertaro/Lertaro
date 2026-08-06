using System.Text;
using Lertaro.Core.Indexer.Mft;

namespace Lertaro.Core.Tests.Indexer.Mft;

[TestClass]
public sealed class MftParserTests
{
    [TestMethod]
    public void ApplyFixup_ReplacesLastTwoBytesOfEachSectorWithUsaEntries()
    {
        const uint bytesPerSector = 512;
        const int recLen = 1024;
        var buf = new byte[recLen];

        const int usaOff = 0x30;
        const int usaCount = 3; // i = 1, 2 -> two sector fixups
        WriteUInt16(buf, 4, usaOff);
        WriteUInt16(buf, 6, usaCount);

        // Fixup entries (what belongs at the end of each sector).
        WriteUInt16(buf, usaOff + 2, 0xAAAA);
        WriteUInt16(buf, usaOff + 4, 0xBBBB);

        // Placeholder USN markers currently sitting in the sector-end slots.
        WriteUInt16(buf, (int)bytesPerSector - 2, 0x1111);
        WriteUInt16(buf, (int)(2 * bytesPerSector) - 2, 0x1111);

        MftParser.ApplyFixup(buf, bytesPerSector, 0, recLen);

        Assert.AreEqual(0xAAAA, ReadUInt16(buf, (int)bytesPerSector - 2));
        Assert.AreEqual(0xBBBB, ReadUInt16(buf, (int)(2 * bytesPerSector) - 2));
    }

    [TestMethod]
    public void ApplyFixup_EntryPastRecordBounds_StopsWithoutThrowing()
    {
        var buf = new byte[64];
        WriteUInt16(buf, 4, 0x10);
        WriteUInt16(buf, 6, 5); // would require sectors far beyond this tiny buffer

        MftParser.ApplyFixup(buf, 512, 0, buf.Length);
    }

    [TestMethod]
    public void ParseDataRuns_SingleRun_ReturnsOneExtent()
    {
        var rec = BuildRecordWithDataRuns((runLen: 5, delta: 10));

        var extents = MftParser.ParseDataRuns(rec);

        Assert.HasCount(1, extents);
        Assert.AreEqual((10L, 5L), extents[0]);
    }

    [TestMethod]
    public void ParseDataRuns_MultipleDataAttributes_AccumulatesExtentsFromAllDataAttributes()
    {
        var rec = BuildRecordWithDataRuns((runLen: 5, delta: 10), (runLen: 3, delta: -4));

        var extents = MftParser.ParseDataRuns(rec);

        Assert.HasCount(2, extents);
        Assert.AreEqual((10L, 5L), extents[0]);
        Assert.AreEqual((6L, 3L), extents[1]);
    }

    [TestMethod]
    public void ParseAttributeListRecordIndexes_ResidentAttributeList_ExtractsTargetAttributeRecordIndexes()
    {
        const int a = 32;
        const int vo = 24;
        var vp = a + vo;
        var entry1Len = 0x18;
        var recLen = vp + entry1Len * 2;
        var buf = new byte[recLen];

        WriteUInt16(buf, 0x14, a);
        WriteUInt32(buf, a, 0x20); // $ATTRIBUTE_LIST
        WriteUInt32(buf, a + 4, (uint)(recLen - a));
        buf[a + 8] = 0; // resident
        WriteUInt32(buf, a + 0x10, (uint)(entry1Len * 2)); // value length
        WriteUInt16(buf, a + 0x14, vo);

        // Entry 1: $DATA (0x80), mftRef = 15
        WriteUInt32(buf, vp, 0x80);
        WriteUInt16(buf, vp + 4, (ushort)entry1Len);
        WriteInt64(buf, vp + 0x10, 15);

        // Entry 2: $DATA (0x80), mftRef = 28
        WriteUInt32(buf, vp + entry1Len, 0x80);
        WriteUInt16(buf, vp + entry1Len + 4, (ushort)entry1Len);
        WriteInt64(buf, vp + entry1Len + 0x10, 28);

        var indexes = MftParser.ParseAttributeListRecordIndexes(buf, 0x80);

        Assert.HasCount(2, indexes);
        Assert.AreEqual(15uL, indexes[0]);
        Assert.AreEqual(28uL, indexes[1]);
    }

    [TestMethod]
    public void ParseAttributeListRecordIndexes_NonResidentAttributeList_ParsesClustersAndExtractsIndexes()
    {
        const int a = 32;
        var buf = new byte[128];
        WriteUInt16(buf, 0x14, a);
        WriteUInt32(buf, a, 0x20); // $ATTRIBUTE_LIST
        WriteUInt32(buf, a + 4, 64);
        buf[a + 8] = 1; // non-resident
        WriteUInt16(buf, a + 0x20, 48); // mpOff (relative to a, so p = 32 + 48 = 80)
        WriteInt64(buf, a + 0x30, 0x18); // realSize = 24 bytes

        // Data run at offset 80: len=1 cluster, lcn=10
        buf[80] = 0x11;
        buf[81] = 1; // runLen = 1 cluster
        buf[82] = 10; // LCN delta = +10

        // Attribute list entry buffer (24 bytes): $DATA (0x80), mftRef = 99
        var attrListBuffer = new byte[24];
        WriteUInt32(attrListBuffer, 0, 0x80);
        WriteUInt16(attrListBuffer, 4, 0x18);
        WriteInt64(attrListBuffer, 0x10, 99);

        var readCalled = false;
        var indexes = MftParser.ParseAttributeListRecordIndexes(buf, 0x80, (off, targetBuf, count) =>
        {
            readCalled = true;
            attrListBuffer.CopyTo(targetBuf, 0);
            return true;
        }, 4096);

        Assert.IsTrue(readCalled);
        Assert.HasCount(1, indexes);
        Assert.AreEqual(99uL, indexes[0]);
    }


    [TestMethod]
    public void ParseDataRuns_NoDataAttribute_ReturnsEmpty()
    {
        var rec = new byte[64];
        WriteUInt16(rec, 0x14, 32);
        WriteUInt32(rec, 32, 0xFFFFFFFF); // immediate end marker

        var extents = MftParser.ParseDataRuns(rec);

        Assert.IsEmpty(extents);
    }

    [TestMethod]
    public void ParseDataRuns_ResidentDataAttribute_IsSkipped()
    {
        const int a = 32;
        var rec = new byte[64];
        WriteUInt16(rec, 0x14, a);
        WriteUInt32(rec, a, 0x80); // $DATA
        WriteUInt32(rec, a + 4, 16); // len, next attribute would start at a+16=48
        rec[a + 8] = 0; // resident -> condition requires ==1, so this attribute is skipped

        var extents = MftParser.ParseDataRuns(rec);

        Assert.IsEmpty(extents);
    }

    [TestMethod]
    public void CollectNames_StandardInfoAndFileNameAndData_PopulatesNamesAndTimestamps()
    {
        var buf = BuildFullRecord(out var expectedName);

        var names = new List<(UInt128 parent, string name, long size)>();
        var stdAttrs = MftParser.CollectNames(buf, 0, buf.Length, names,
            out var creationTimeUtc, out var lastWriteTimeUtc, out var lastAccessTimeUtc);

        Assert.HasCount(1, names);
        Assert.AreEqual((UInt128)777, names[0].parent);
        Assert.AreEqual(expectedName, names[0].name);
        Assert.AreEqual(99999L, names[0].size); // overridden by $DATA's real size, not the stale $FILE_NAME size
        Assert.AreEqual(0x21u, stdAttrs);
        Assert.AreEqual(111L, creationTimeUtc);
        Assert.AreEqual(222L, lastWriteTimeUtc);
        Assert.AreEqual(333L, lastAccessTimeUtc);
    }

    [TestMethod]
    public void CollectNames_DosOnlyShortName_UsesFallback()
    {
        const int a = 32;
        const int vo = 24;
        var vp = a + vo;
        const string name = "abcd";
        var recLen = vp + 0x42 + name.Length * 2;
        var buf = new byte[recLen];

        WriteUInt16(buf, 0x14, a);
        WriteUInt32(buf, a, 0x30); // $FILE_NAME
        WriteUInt32(buf, a + 4, (uint)(recLen - a)); // len covers the rest of the buffer
        buf[a + 8] = 0; // resident
        WriteUInt16(buf, a + 0x14, vo);
        WriteInt64(buf, vp, 1); // parent
        WriteInt64(buf, vp + 0x30, 1); // size
        buf[vp + 0x40] = (byte)name.Length;
        buf[vp + 0x41] = 2; // DOS-only namespace -> fallback as last resort when no Win32 name
        WriteBytes(buf, vp + 0x42, Encoding.Unicode.GetBytes(name));

        var names = new List<(UInt128 parent, string name, long size)>();
        MftParser.CollectNames(buf, 0, buf.Length, names, out _, out _, out _);

        Assert.HasCount(1, names);
        Assert.AreEqual(name, names[0].name);
    }

    private static byte[] BuildFullRecord(out string expectedName)
    {
        expectedName = "doc.txt";

        const int a1 = 32; // $STANDARD_INFORMATION
        const int len1 = 96;
        const int vo1 = 24;
        var vp1 = a1 + vo1;

        const int a2 = a1 + len1; // $FILE_NAME
        const int vo2 = 24;
        var vp2 = a2 + vo2;
        var nameBytes = Encoding.Unicode.GetBytes(expectedName);
        var len2 = (vp2 + 0x42 + nameBytes.Length) - a2;

        var a3 = a2 + len2; // $DATA
        const int len3 = 0x38;

        var recLen = a3 + len3;
        var buf = new byte[recLen];

        WriteUInt16(buf, 0x14, a1);

        // $STANDARD_INFORMATION
        WriteUInt32(buf, a1, 0x10);
        WriteUInt32(buf, a1 + 4, len1);
        buf[a1 + 8] = 0; // resident
        WriteUInt16(buf, a1 + 0x14, vo1);
        WriteInt64(buf, vp1 + 0x00, 111); // creationTimeUtc
        WriteInt64(buf, vp1 + 0x08, 222); // lastWriteTimeUtc
        WriteInt64(buf, vp1 + 0x18, 333); // lastAccessTimeUtc
        WriteUInt32(buf, vp1 + 0x20, 0x21); // stdAttrs

        // $FILE_NAME
        WriteUInt32(buf, a2, 0x30);
        WriteUInt32(buf, a2 + 4, (uint)len2);
        buf[a2 + 8] = 0; // resident
        WriteUInt16(buf, a2 + 0x14, vo2);
        WriteInt64(buf, vp2, 777); // parent
        WriteInt64(buf, vp2 + 0x30, 55555); // stale size, must be overridden by $DATA's size
        buf[vp2 + 0x40] = (byte)(nameBytes.Length / 2);
        buf[vp2 + 0x41] = 1; // Win32 namespace -> included
        WriteBytes(buf, vp2 + 0x42, nameBytes);

        // $DATA (unnamed, non-resident)
        WriteUInt32(buf, a3, 0x80);
        WriteUInt32(buf, a3 + 4, len3);
        buf[a3 + 8] = 1; // non-resident
        buf[a3 + 9] = 0; // unnamed stream
        WriteInt64(buf, a3 + 0x30, 99999); // dataSize

        return buf;
    }

    private static byte[] BuildRecordWithDataRuns(params (long runLen, long delta)[] runs)
    {
        const int a = 32;
        const int mpOff = 40; // relative to a

        var runBytes = new List<byte>();
        foreach (var (runLen, delta) in runs)
        {
            runBytes.Add(0x11); // lenBytes=1, offBytes=1
            runBytes.Add((byte)runLen);
            runBytes.Add(unchecked((byte)delta));
        }
        runBytes.Add(0x00); // terminator

        var len = mpOff + runBytes.Count + 4;
        var recLen = a + len + 16;
        var buf = new byte[recLen];

        WriteUInt16(buf, 0x14, a);
        WriteUInt32(buf, a, 0x80); // $DATA
        WriteUInt32(buf, a + 4, (uint)len);
        buf[a + 8] = 1; // non-resident
        WriteUInt16(buf, a + 0x20, mpOff);
        WriteBytes(buf, a + mpOff, runBytes.ToArray());

        return buf;
    }

    private static void WriteUInt16(byte[] buf, int offset, int value) =>
        BitConverter.GetBytes((ushort)value).CopyTo(buf, offset);

    private static void WriteUInt32(byte[] buf, int offset, uint value) =>
        BitConverter.GetBytes(value).CopyTo(buf, offset);

    private static void WriteInt64(byte[] buf, int offset, long value) =>
        BitConverter.GetBytes(value).CopyTo(buf, offset);

    private static void WriteBytes(byte[] buf, int offset, byte[] value) =>
        value.CopyTo(buf, offset);

    private static int ReadUInt16(byte[] buf, int offset) =>
        BitConverter.ToUInt16(buf, offset);
}
