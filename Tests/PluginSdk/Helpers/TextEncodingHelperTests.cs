using System.Text;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.PluginSdk.Tests.Helpers;

[TestClass]
public sealed class TextEncodingHelperTests
{
    [TestMethod]
    public void DecodeText_GbkWithoutBom_DecodesChinese()
    {
        // "这是文本文档" in GBK: D5E2 CAC7 CEC4 B1BE CEC4 B5B5; these bytes are invalid UTF-8.
        var gbkBytes = new byte[]
        {
            0xD5, 0xE2, 0xCA, 0xC7, 0xCE, 0xC4, 0xB1, 0xBE, 0xCE, 0xC4, 0xB5, 0xB5
        };

        Assert.AreEqual("这是文本文档", TextEncodingHelper.DecodeText(gbkBytes));
    }

    [TestMethod]
    public void DecodeText_Utf8Bom_DecodesWithoutBomArtifact()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("价格bom")).ToArray();

        Assert.AreEqual("价格bom", TextEncodingHelper.DecodeText(bytes));
    }

    [TestMethod]
    public void DecodeText_Utf8WithoutBom_StaysUtf8()
    {
        var bytes = Encoding.UTF8.GetBytes("plain ascii with 中文");

        Assert.AreEqual("plain ascii with 中文", TextEncodingHelper.DecodeText(bytes));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void DecodeText_Utf16Bom_DecodesChinese(bool littleEndian)
    {
        var encoding = littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("中文ok")).ToArray();

        Assert.AreEqual("中文ok", TextEncodingHelper.DecodeText(bytes));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void DecodeText_BomlessUtf16_DecodesChinese(bool littleEndian)
    {
        var encoding = littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
        var bytes = encoding.GetBytes("bomless utf16 text");

        Assert.AreEqual("bomless utf16 text", TextEncodingHelper.DecodeText(bytes));
    }

    [TestMethod]
    public void DecodeText_EmptyInput_ReturnsEmpty() =>
        Assert.AreEqual(string.Empty, TextEncodingHelper.DecodeText(Array.Empty<byte>()));

    [TestMethod]
    public void DecodeText_Utf8ChunkCutMidCharacter_StillDecodesAsUtf8()
    {
        // A read that lands inside a 3-byte UTF-8 char must not be mistaken for GBK.
        var bytes = Encoding.UTF8.GetBytes(new string('a', 4095) + "中").Take(4097).ToArray();

        var text = TextEncodingHelper.DecodeText(bytes, bytes.Length, hasMoreBytes: true);

        Assert.AreEqual(new string('a', 4095), text);
    }

    [TestMethod]
    public void LooksBinary_NulBytesInBothLanes_ReturnsTrue()
    {
        var binary = new byte[256];
        for (var i = 0; i < binary.Length; i += 2) binary[i] = 0x00;
        for (var i = 1; i < binary.Length; i += 4) binary[i] = 0x00;

        Assert.IsTrue(TextEncodingHelper.LooksBinary(binary));
    }

    [TestMethod]
    public void LooksBinary_NulFreeText_ReturnsFalse() =>
        Assert.IsFalse(TextEncodingHelper.LooksBinary(Encoding.UTF8.GetBytes("plain text file\r\n")));

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void LooksBinary_Utf16WithBom_ReturnsFalse(bool littleEndian)
    {
        var encoding = littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("utf16 text")).ToArray();

        Assert.IsTrue(TextEncodingHelper.HasUnicodeBom(bytes));
        Assert.IsFalse(TextEncodingHelper.LooksBinary(bytes));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void LooksBinary_BomlessUtf16_ReturnsFalse(bool littleEndian)
    {
        var encoding = littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
        var bytes = encoding.GetBytes("bomless utf16 text");

        Assert.IsFalse(TextEncodingHelper.LooksBinary(bytes));
    }
}
