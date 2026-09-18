using System.Text;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The byte-native encoder is documented as verified byte-identical to the string-path combination
// generator -- these tests lean on that invariant directly (differential testing against
// PinyinAliasCombinationGenerator.GenerateAliases) rather than hardcoding exact pinyin spellings.
// Conversion is passed in explicitly so each test states which alias set it is comparing.
[TestClass]
public sealed class PinyinAliasUtf8EncoderTests
{
    // A fake converter for the public 電 -> 电 pair -- the same character the repo's own tests use for
    // Traditional text -- so the pinyin comparisons below stay independent of the OS mapping (the real
    // call is exercised in HanConversionTests).
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
    [DataRow("中国人")]
    [DataRow("北京市")]
    [DataRow("abc")]
    [DataRow("中abc")]
    [DataRow("中国人民")]
    [DataRow("電腦")]
    public void Encode_WithConversionOff_MatchesStringPathCombinationGenerator(string text)
    {
        var sink = new AliasByteSink();
        PinyinAliasUtf8Encoder.Encode(text, sink, false, FakeConvert);
        var decoded = DecodeSegments(sink);

        var expected = PinyinAliasCombinationGenerator.GenerateAliases(text);

        CollectionAssert.AreEquivalent(expected, decoded);
    }

    [TestMethod]
    public void Encode_TraditionalNameWithConversionOn_AddsTheSimplifiedSegment()
    {
        var sink = new AliasByteSink();
        PinyinAliasUtf8Encoder.Encode("電腦", sink, true, FakeConvert);
        var decoded = DecodeSegments(sink);

        var expected = PinyinAliasCombinationGenerator.GenerateAliases("電腦")
            .Concat(new[] { "电脑" })
            .ToList();

        CollectionAssert.AreEquivalent(expected, decoded);
    }

    [TestMethod]
    public void Encode_SingleTraditionalCharWithConversionOn_EmitsPinyinAndTheSimplifiedChar()
    {
        // The single-character input takes an early return in the pinyin encoder, so the Simplified
        // segment has to be added outside it or this shape would silently lose its alias.
        var sink = new AliasByteSink();
        PinyinAliasUtf8Encoder.Encode("電", sink, true, FakeConvert);

        var expected = PinyinAliasCombinationGenerator.GenerateAliases("電")
            .Concat(new[] { "电" })
            .ToList();

        CollectionAssert.AreEquivalent(expected, DecodeSegments(sink));
    }

    [TestMethod]
    public void Encode_SimplifiedNameWithConversionOn_EmitsNoExtraSegment()
    {
        // Nothing to add when the conversion is a no-op, which is every Simplified name.
        var sink = new AliasByteSink();
        PinyinAliasUtf8Encoder.Encode("中国", sink, true, FakeConvert);

        var expected = PinyinAliasCombinationGenerator.GenerateAliases("中国");

        CollectionAssert.AreEquivalent(expected, DecodeSegments(sink));
    }

    [TestMethod]
    public void Encode_EmptyText_ProducesNoSegments()
    {
        var sink = new AliasByteSink();
        PinyinAliasUtf8Encoder.Encode("", sink, true, FakeConvert);

        Assert.AreEqual(0, sink.SegmentCount);
    }

    [TestMethod]
    public void Encode_SurrogatePairEmoji_DoesNotCorruptSurroundingChars()
    {
        var text = "中😀国";
        var sink = new AliasByteSink();
        PinyinAliasUtf8Encoder.Encode(text, sink, false, FakeConvert);
        var decoded = DecodeSegments(sink);

        var expected = PinyinAliasCombinationGenerator.GenerateAliases(text);
        CollectionAssert.AreEquivalent(expected, decoded);
    }
}
