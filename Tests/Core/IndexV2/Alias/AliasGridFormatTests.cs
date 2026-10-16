using Lertaro.Core.IndexV2.Alias;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.Tests.IndexV2.Alias;

[TestClass]
public sealed class AliasGridFormatTests
{
    private static byte[] Encode(FakeGridProvider provider, string text, out AliasGridFormat.Index index)
    {
        var sink = new AliasByteSink();
        var length = AliasGridFormat.Encode(text, provider, sink);
        Assert.IsTrue(length > 0, "a contract-satisfying provider must encode");
        Assert.AreEqual(length, sink.Segment(0).Length);

        var payload = sink.Segment(0).ToArray();
        index = new AliasGridFormat.Index();
        Assert.IsTrue(index.TryBind(payload), "a payload this encoder wrote must bind");
        return payload;
    }

    [TestMethod]
    public void StartsFrom_ReportsOnlyTheFirstUnitOfEachReading()
    {
        // First units: f, d, 7, P. The letters inside a reading (a, n, g) and letters this name never
        // begins a syllable with (x) are not covered, and the guard that reads the mask has to say so --
        // the initials walk would otherwise be asked for every candidate that cannot possibly match.
        var sets = new[] { new[] { "fang" }, new[] { "dan", "duo" }, new[] { "7" }, new[] { "PDF" } };
        Encode(new FakeGridProvider(sets, new[] { false, true, false, true }), "abcd", out var index);

        Assert.IsTrue(index.StartsFrom('f'));
        Assert.IsTrue(index.StartsFrom('d'));
        Assert.IsTrue(index.StartsFrom('7'), "a passed-through digit is a reading that starts with it");
        Assert.IsTrue(index.StartsFrom('P'), "and so is an uppercase run the provider did not transliterate");
        Assert.IsFalse(index.StartsFrom('a'), "'a' occurs inside fang, which is not a place a piece starts");
        Assert.IsFalse(index.StartsFrom('x'));
        Assert.IsTrue(index.StartsFromAll("fd7P".AsSpan()));
        Assert.IsFalse(index.StartsFromAll("fdx".AsSpan()), "one uncovered letter is enough to refuse the term");
    }

    [TestMethod]
    public void StartsFrom_RefusesAboveTheRangeUntilAReadingStartsThereToo()
    {
        Encode(new FakeGridProvider(new[] { new[] { "fang" }, new[] { "dan" } }), "ab", out var ascii);
        Assert.IsFalse(ascii.StartsFrom('日'), "no reading of an all-ASCII grid can begin with a Han character");

        // A name that carries an untransliterated character is a reading that starts above the tracked
        // range, and a 128-bit mask cannot represent it -- so the answer becomes "ask the walk", never "no".
        var wide = new FakeGridProvider(new[] { new[] { "fang" }, new[] { "、" } });
        Encode(wide, "ab", out var withWide);
        Assert.IsTrue(withWide.StartsFrom('x'));
        Assert.IsTrue(withWide.StartsFrom('、'));
    }

    [TestMethod]
    public void Encode_MixedReadings_RoundTripsEveryPosition()
    {
        var sets = new[]
        {
            new[] { "fang" },
            new[] { "dan", "duo" },
            new[] { "\uD83D" },          // a lone high surrogate half
            new[] { "\uDE00" },          // and its low half
            new[] { "é" },               // a two-byte literal
            new[] { "\u0001" },          // the pipe placeholder the alias string path substitutes
        };
        var boundaries = new[] { false, true, false, false, true, false };

        Encode(new FakeGridProvider(sets, boundaries), "abcdef", out var index);

        Assert.AreEqual(6, index.PositionCount);
        Assert.AreEqual(1, index.ReadingCount(0));
        Assert.AreEqual(2, index.ReadingCount(1));
        Assert.AreEqual("dan", index.Reading(1, 0).ToString());
        Assert.AreEqual("duo", index.Reading(1, 1).ToString());
        Assert.AreEqual("\uD83D", index.Reading(2, 0).ToString());
        Assert.AreEqual("\uDE00", index.Reading(3, 0).ToString());
        Assert.AreEqual("é", index.Reading(4, 0).ToString());
        Assert.AreEqual("\u0001", index.Reading(5, 0).ToString());

        Assert.IsFalse(index.BoundaryBefore(0));
        Assert.IsTrue(index.BoundaryBefore(1));
        Assert.IsFalse(index.BoundaryBefore(2));
        Assert.IsTrue(index.BoundaryBefore(4));
        Assert.IsFalse(index.BoundaryBefore(5));
    }

    [TestMethod]
    public void Encode_PolyphonicLongName_StaysLinearInsteadOfEnumerating()
    {
        // 44 positions, two readings of five units each: the alias space holds 2^44 spellings, of which
        // the string encoding would bake 32 (the combination cap) -- the case that produced the 7807-byte
        // blob this format exists to replace.
        var sets = new string[44][];
        var boundaries = new bool[44];
        for (var i = 0; i < sets.Length; i++)
        {
            sets[i] = new[] { "zhong", "chong" };
            boundaries[i] = i > 0;
        }

        var payload = Encode(new FakeGridProvider(sets, boundaries), new string('a', 44), out var index);

        Assert.AreEqual(44, index.PositionCount);
        var enumerated = 32 * (44 * 5 + 43); // what the capped string enumeration bakes for this name
        Assert.IsTrue(
            payload.Length * 8 < enumerated,
            $"grid {payload.Length} bytes vs {enumerated} enumerated bytes");
    }

    [TestMethod]
    public void TryBind_LongPayload_KeepsEveryReadingIntact()
    {
        var sets = new string[44][];
        var boundaries = new bool[44];
        for (var i = 0; i < sets.Length; i++)
        {
            sets[i] = new[] { "zhong", "chong" };
            boundaries[i] = i > 0;
        }

        Encode(new FakeGridProvider(sets, boundaries), new string('a', 44), out var index);

        Assert.AreEqual(44, index.PositionCount);
        for (var p = 0; p < index.PositionCount; p++)
        {
            Assert.AreEqual(2, index.ReadingCount(p));
            Assert.AreEqual("zhong", index.Reading(p, 0).ToString(), $"position {p} reading 0");
            Assert.AreEqual("chong", index.Reading(p, 1).ToString(), $"position {p} reading 1");
        }
    }

    [TestMethod]
    public void TryBind_WrongFormatTag_Refuses()
    {
        var payload = Encode(new FakeGridProvider(new[] { new[] { "a" } }), "a", out _);
        payload[0] = 9;

        Assert.IsFalse(new AliasGridFormat.Index().TryBind(payload));
    }

    [TestMethod]
    public void TryBind_TruncatedBoundarySection_Refuses()
    {
        var sets = new string[44][];
        var boundaries = new bool[44];
        for (var i = 0; i < sets.Length; i++)
        {
            sets[i] = new[] { "zhong", "chong" };
            boundaries[i] = i > 0;
        }

        var payload = Encode(new FakeGridProvider(sets, boundaries), new string('a', 44), out _);

        Assert.IsFalse(new AliasGridFormat.Index().TryBind(payload.AsSpan(0, payload.Length - 3)));
    }

    [TestMethod]
    public void TryBind_ZeroReadingsAtAPosition_Refuses()
    {
        // tag, one position, reading count 0, one boundary byte
        byte[] payload = { AliasGridFormat.FormatTag, 1, 0, 0 };

        Assert.IsFalse(new AliasGridFormat.Index().TryBind(payload));
    }

    [TestMethod]
    public void TryBind_AbsurdPositionCount_Refuses()
    {
        // varint 5000: 0x88 0x27 -- far past any legal file name
        byte[] payload = { AliasGridFormat.FormatTag, 0x88, 0x27 };

        Assert.IsFalse(new AliasGridFormat.Index().TryBind(payload));
    }

    [TestMethod]
    public void Encode_EmptyText_ProducesNothing()
    {
        var sink = new AliasByteSink();

        Assert.AreEqual(0, AliasGridFormat.Encode(string.Empty, new FakeGridProvider(Array.Empty<string[]>()), sink));
        Assert.AreEqual(0, sink.SegmentCount);
    }

    [TestMethod]
    public void Encode_ProviderWithTooFewReadingSets_KeepsStringAliases()
    {
        var sink = new AliasByteSink();

        Assert.AreEqual(0, AliasGridFormat.Encode("ab", new FakeGridProvider(new[] { new[] { "a" } }), sink));
        Assert.AreEqual(0, sink.SegmentCount, "a refused encode must not leave a half-written segment");
    }
}
