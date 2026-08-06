using System.Text;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The byte-native encoder is documented as verified byte-identical to the string-path combination
// generator -- these tests lean on that invariant directly (differential testing against
// PinyinAliasCombinationGenerator.GenerateAliases) rather than hardcoding exact pinyin spellings.
[TestClass]
public sealed class PinyinAliasUtf8EncoderTests
{
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
    public void Encode_MatchesStringPathCombinationGenerator(string text)
    {
        var sink = new AliasByteSink();
        PinyinAliasUtf8Encoder.Encode(text, sink);
        var decoded = DecodeSegments(sink);

        var expected = PinyinAliasCombinationGenerator.GenerateAliases(text);

        CollectionAssert.AreEquivalent(expected, decoded);
    }

    [TestMethod]
    public void Encode_EmptyText_ProducesNoSegments()
    {
        var sink = new AliasByteSink();
        PinyinAliasUtf8Encoder.Encode("", sink);

        Assert.AreEqual(0, sink.SegmentCount);
    }

    [TestMethod]
    public void Encode_SurrogatePairEmoji_DoesNotCorruptSurroundingChars()
    {
        var text = "中😀国";
        var sink = new AliasByteSink();
        PinyinAliasUtf8Encoder.Encode(text, sink);
        var decoded = DecodeSegments(sink);

        var expected = PinyinAliasCombinationGenerator.GenerateAliases(text);
        CollectionAssert.AreEquivalent(expected, decoded);
    }
}
