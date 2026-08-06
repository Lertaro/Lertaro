using Lertaro.Core.Wire;

namespace Lertaro.Core.Tests.Wire;

[TestClass]
public sealed class VarIntStringCodecTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(127)]   // largest single-byte value (7 bits)
    [DataRow(128)]   // smallest value needing a second byte
    [DataRow(16383)] // largest two-byte value (14 bits)
    [DataRow(16384)] // smallest value needing a third byte
    [DataRow(int.MaxValue)]
    public void SevenBitEncodedInt_RoundTrips(int value)
    {
        var buffer = new byte[10];
        var writeOffset = 0;
        VarIntStringCodec.Write7BitEncodedInt(buffer, ref writeOffset, value);

        var readOffset = 0;
        var result = VarIntStringCodec.Read7BitEncodedInt(buffer, ref readOffset);

        Assert.AreEqual(value, result);
        Assert.AreEqual(writeOffset, readOffset);
    }

    [TestMethod]
    public void WriteString_ThenReadString_RoundTrips()
    {
        var buffer = new byte[256];
        var writeOffset = 0;
        VarIntStringCodec.WriteString(buffer, ref writeOffset, "lertaro");

        var readOffset = 0;
        var result = VarIntStringCodec.ReadString(buffer, ref readOffset);

        Assert.AreEqual("lertaro", result);
        Assert.AreEqual(writeOffset, readOffset);
    }

    [TestMethod]
    public void WriteString_NullString_RoundTripsAsEmpty()
    {
        var buffer = new byte[16];
        var writeOffset = 0;
        VarIntStringCodec.WriteString(buffer, ref writeOffset, null);

        var readOffset = 0;
        var result = VarIntStringCodec.ReadString(buffer, ref readOffset);

        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void WriteString_UnicodeText_RoundTripsExactly()
    {
        const string text = "文件搜索 🔍";
        var buffer = new byte[256];
        var writeOffset = 0;
        VarIntStringCodec.WriteString(buffer, ref writeOffset, text);

        var readOffset = 0;
        var result = VarIntStringCodec.ReadString(buffer, ref readOffset);

        Assert.AreEqual(text, result);
    }

    [TestMethod]
    public void WriteString_AdvancesOffsetPastPreviousData_ForConsecutiveWrites()
    {
        var buffer = new byte[256];
        var writeOffset = 0;
        VarIntStringCodec.WriteString(buffer, ref writeOffset, "first");
        VarIntStringCodec.WriteString(buffer, ref writeOffset, "second");

        var readOffset = 0;
        var first = VarIntStringCodec.ReadString(buffer, ref readOffset);
        var second = VarIntStringCodec.ReadString(buffer, ref readOffset);

        Assert.AreEqual("first", first);
        Assert.AreEqual("second", second);
    }
}
