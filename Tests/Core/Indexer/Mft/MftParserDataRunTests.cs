using Lertaro.Core.Indexer.Mft;

namespace Lertaro.Core.Tests.Indexer.Mft;

// Split out of MftParserTests purely to keep that file under the repo's per-file line limit; holds
// the $DATA run-list parsing half of MftParser's coverage, including the bounds-check regressions.
[TestClass]
public sealed class MftParserDataRunTests
{
    [TestMethod]
    public void SingleRun_ReturnsOneExtent()
    {
        var rec = BuildRecordWithDataRuns((runLen: 5, delta: 10));

        var extents = MftParser.ParseDataRuns(rec);

        Assert.HasCount(1, extents);
        Assert.AreEqual((10L, 5L), extents[0]);
    }

    [TestMethod]
    public void MultipleDataAttributes_AccumulatesExtentsFromAllDataAttributes()
    {
        var rec = BuildRecordWithDataRuns((runLen: 5, delta: 10), (runLen: 3, delta: -4));

        var extents = MftParser.ParseDataRuns(rec);

        Assert.HasCount(2, extents);
        Assert.AreEqual((10L, 5L), extents[0]);
        Assert.AreEqual((6L, 3L), extents[1]);
    }

    [TestMethod]
    public void NoDataAttribute_ReturnsEmpty()
    {
        var rec = new byte[64];
        WriteUInt16(rec, 0x14, 32);
        WriteUInt32(rec, 32, 0xFFFFFFFF); // immediate end marker

        var extents = MftParser.ParseDataRuns(rec);

        Assert.IsEmpty(extents);
    }

    [TestMethod]
    public void ResidentDataAttribute_IsSkipped()
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

    // Regression: a malformed run header whose length field claims more bytes than the record still
    // holds used to read past the buffer (the inner loop lacked ParseDataRunsFromAttribute's bounds
    // check) and could throw on a torn record.
    [TestMethod]
    public void RunLengthFieldRunsPastRecord_TruncatesWithoutThrowing()
    {
        const int a = 32;
        const int mpOff = 63; // header byte sits at the record's last offset (95)
        var rec = new byte[96];
        WriteUInt16(rec, 0x14, a);
        WriteUInt32(rec, a, 0x80);
        WriteUInt32(rec, a + 4, 64); // attribute runs to the end of the record
        rec[a + 8] = 1; // non-resident
        WriteUInt16(rec, a + 0x20, mpOff);
        rec[a + mpOff] = 0x24; // lenBytes=2, offBytes=4 -> needs 6 more bytes, only 0 remain

        var extents = MftParser.ParseDataRuns(rec);

        Assert.IsEmpty(extents);
    }

    [TestMethod]
    public void RunOffsetFieldRunsPastRecord_TruncatesWithoutThrowing()
    {
        const int a = 32;
        const int mpOff = 62; // header at 94, one length byte at 95, offset field would start past the end
        var rec = new byte[96];
        WriteUInt16(rec, 0x14, a);
        WriteUInt32(rec, a, 0x80);
        WriteUInt32(rec, a + 4, 64);
        rec[a + 8] = 1; // non-resident
        WriteUInt16(rec, a + 0x20, mpOff);
        rec[a + mpOff] = 0x12; // lenBytes=1 (fits), offBytes=2 -> 96+2 > 96
        rec[a + mpOff + 1] = 3; // runLen byte

        var extents = MftParser.ParseDataRuns(rec);

        Assert.IsEmpty(extents);
    }

    [TestMethod]
    public void RunFieldsOutsideAttribute_StopAtAttributeBoundary()
    {
        const int a = 32;
        const int mpOff = 46; // run-list header starts at offset 78; the attribute ends at 80
        var rec = new byte[96];
        WriteUInt16(rec, 0x14, a);
        WriteUInt32(rec, a, 0x80);
        WriteUInt32(rec, a + 4, 48); // attribute ends before the complete run fields
        rec[a + 8] = 1; // non-resident
        WriteUInt16(rec, a + 0x20, mpOff);
        rec[a + mpOff] = 0x12; // lenBytes=2, offBytes=1; the second length byte is outside the attribute
        rec[a + mpOff + 1] = 1;
        rec[a + mpOff + 2] = 0;
        rec[a + mpOff + 3] = 1;

        var extents = MftParser.ParseDataRuns(rec);
        var attributeExtents = new List<(long lcn, long clusters)>();
        MftParser.ParseDataRunsFromAttribute(rec, a, attributeExtents);

        Assert.IsEmpty(extents);
        Assert.IsEmpty(attributeExtents);
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
        runBytes.ToArray().CopyTo(buf, a + mpOff);

        return buf;
    }

    private static void WriteUInt16(byte[] buf, int offset, int value) =>
        BitConverter.GetBytes((ushort)value).CopyTo(buf, offset);

    private static void WriteUInt32(byte[] buf, int offset, uint value) =>
        BitConverter.GetBytes(value).CopyTo(buf, offset);
}
