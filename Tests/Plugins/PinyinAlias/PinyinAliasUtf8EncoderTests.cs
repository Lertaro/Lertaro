using System.Text;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// Byte-native GetAliasesUtf8 now emits exactly one thing: the Simplified spelling of a Traditional
// name -- the only alias this provider still bakes, now that the readings are decided live.
[TestClass]
public sealed class PinyinAliasUtf8EncoderTests
{
    // A fake converter for the public 電 -> 电 pair -- the same character the repo's own tests use for
    // Traditional text -- so the comparisons stay independent of the OS mapping.
    private static string FakeConvert(string text) => text.Replace('電', '电').Replace('腦', '脑');

    private static List<string> DecodeSegments(AliasByteSink sink)
    {
        var result = new List<string>(sink.SegmentCount);
        for (var i = 0; i < sink.SegmentCount; i++)
            result.Add(Encoding.UTF8.GetString(sink.Segment(i)));
        return result;
    }

    [TestMethod]
    [DataRow("中")]
    [DataRow("中国")]
    [DataRow("中国人民")]
    [DataRow("中abc")]
    [DataRow("中😀国")]
    public void Encode_NonTraditionalText_EmitsNothing(string text)
    {
        var sink = new AliasByteSink();
        PinyinAliasUtf8Encoder.Encode(text, sink, convertEnabled: true, FakeConvert);

        Assert.AreEqual(0, sink.SegmentCount, "no reading aliases are baked any more");
    }

    [TestMethod]
    public void Encode_WithConversionOff_EmitsNothingEvenForTraditionalText()
    {
        var sink = new AliasByteSink();
        PinyinAliasUtf8Encoder.Encode("電腦", sink, convertEnabled: false, FakeConvert);

        Assert.AreEqual(0, sink.SegmentCount);
    }

    [TestMethod]
    public void Encode_TraditionalName_EmitsTheSimplifiedSpelling()
    {
        var sink = new AliasByteSink();
        PinyinAliasUtf8Encoder.Encode("電腦中", sink, convertEnabled: true, FakeConvert);

        CollectionAssert.AreEqual(new[] { "电脑中" }, DecodeSegments(sink));
    }

    [TestMethod]
    public void Encode_EmptyText_ProducesNoSegments()
    {
        var sink = new AliasByteSink();
        PinyinAliasUtf8Encoder.Encode("", sink, convertEnabled: true, FakeConvert);

        Assert.AreEqual(0, sink.SegmentCount);
    }
}
