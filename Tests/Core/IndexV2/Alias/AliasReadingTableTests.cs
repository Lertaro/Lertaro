using System.Text;
using Lertaro.Core.IndexV2.Alias;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.Tests.IndexV2.Alias;

// The table-backed grid: readings a payload cannot carry inline are stored once per snapshot and
// referenced by id (see AliasReadingTable). These cover the round trip, the size claim, determinism, and
// that a payload whose ids do not resolve refuses to bind instead of reading past the table.
[TestClass]
public sealed class AliasReadingTableTests
{
    private sealed class StubGridProvider : IAliasGridProvider
    {
        private readonly string[][] _sets;

        public StubGridProvider(string[][] sets) => _sets = sets;

        public bool CanHandle(string text) => true;

        public IReadOnlyList<(char Start, char End)> InputRanges => new[] { ('\u4E00', '\u9FFF') };

        public IReadOnlyList<(char Start, char End)> OutputRanges => new[] { ('a', 'z') };

        public char SyllableSeparator => '\u0002';

        public string[][] GetReadingSets(string text) => _sets;

        public bool NeedsSeparatorBefore(string text, int index) => index > 0;

        public void GetReadingAliasesUtf8(string text, AliasByteSink dest)
        {
        }

        public void GetStandaloneAliasesUtf8(string text, AliasByteSink dest)
        {
        }

        public IEnumerable<string> GetAliases(string text) => Array.Empty<string>();
    }

    private static AliasReadingTable BuildTable(params string[][] sets)
    {
        var builder = new AliasReadingTable.Builder();
        foreach (var set in sets)
            foreach (var reading in set)
                builder.Add(reading);
        return builder.Build();
    }

    private static AliasReadingTable BindTable(AliasReadingTable table)
    {
        var sink = new AliasByteSink();
        table.WritePayload(sink);
        var bound = new AliasReadingTable();
        Assert.IsTrue(bound.TryBind(sink.Segment(0)), "a table this writer produced must bind");
        return bound;
    }

    [TestMethod]
    public void Collect_KeepsRealReadingsAndLeavesSingleAsciiUnitsOut()
    {
        var table = BuildTable(["chong", "zhong"], ["a"], ["."], ["\uD83E\uDE78"]);

        // "a" and "." are single ASCII units: the payload carries those itself, so an entry would only
        // cost bytes. Everything else -- a syllable, a lone astral pair -- is shared.
        Assert.AreEqual(3, table.Count);
        CollectionAssert.AreEqual(new[] { "chong", "zhong", "\uD83E\uDE78" }, new[]
        {
            table.Reading(0).ToString(), table.Reading(1).ToString(), table.Reading(2).ToString(),
        });
    }

    [TestMethod]
    public void Encode_RoundTripsThroughIdsAndLiterals()
    {
        var sets = new[] { new[] { "chong", "zhong" }, new[] { "a" }, new[] { "bei" }, new[] { "." } };
        var provider = new StubGridProvider(sets);
        // The writer encodes against the table it built; the reader resolves ids against the one bound
        // from the payload that table was written into (same ids, same order -- see Build_SameReadings_...).
        var built = BuildTable(sets);
        var bound = BindTable(built);

        var sink = new AliasByteSink();
        Assert.IsTrue(AliasGridFormat.Encode("xxxx", provider, sink, built) > 0);

        var index = new AliasGridFormat.Index();
        Assert.IsTrue(index.TryBind(sink.Segment(0), bound));
        Assert.AreEqual(4, index.PositionCount);
        Assert.AreEqual("chong", index.Reading(0, 0).ToString());
        Assert.AreEqual("zhong", index.Reading(0, 1).ToString());
        Assert.AreEqual("a", index.Reading(1, 0).ToString(), "a single ASCII unit travels inline");
        Assert.AreEqual("bei", index.Reading(2, 0).ToString());
        Assert.AreEqual(".", index.Reading(3, 0).ToString());
        Assert.IsTrue(index.BoundaryBefore(1), "boundaries live in the payload, not the table");

        // The first-letter mask is built while binding, so the table-backed shape has to feed it from the
        // readings it resolves by id, not only from the units it carries inline.
        Assert.IsTrue(index.StartsFrom('c'), "resolved by id");
        Assert.IsTrue(index.StartsFrom('.'), "carried inline");
        Assert.IsFalse(index.StartsFrom('f'), "no reading of this grid begins with f");
    }

    [TestMethod]
    public void Encode_SharesOneReadingAcrossNamesAndPositions()
    {
        var sets = new[] { new[] { "ji" }, new[] { "ji" }, new[] { "zhong" } };
        var provider = new StubGridProvider(sets);
        var table = BuildTable(sets);

        var withTable = new AliasByteSink();
        var selfContained = new AliasByteSink();
        AliasGridFormat.Encode("xxx", provider, withTable, table);
        AliasGridFormat.Encode("xxx", provider, selfContained);

        // Three readings, two of them the same: the table spells "ji" once, the self-contained payload
        // spells it twice.
        Assert.IsTrue(withTable.Segment(0).Length < selfContained.Segment(0).Length,
            $"table-backed {withTable.Segment(0).Length} bytes vs self-contained {selfContained.Segment(0).Length}");
    }

    [TestMethod]
    public void Encode_ReadingMissingFromTheTable_WritesNothing()
    {
        var provider = new StubGridProvider(new[] { new[] { "chong" } });
        var table = BuildTable(["zhong"]);

        var sink = new AliasByteSink();
        Assert.AreEqual(0, AliasGridFormat.Encode("x", provider, sink, table));
        Assert.AreEqual(0, sink.SegmentCount, "a payload it cannot express must leave nothing behind");
    }

    [TestMethod]
    public void Bind_IdPastTheEndOfTheTable_Refuses()
    {
        // "zzz" sorts last, so its id sits well past the front of the table: a payload carrying it cannot
        // be resolved against a table that stops earlier.
        var sets = new[] { new[] { "zzz" } };
        var provider = new StubGridProvider(sets);
        var built = BuildTable(sets.Concat(new[] { new[] { "a", "b", "c", "d", "e", "f", "g", "h" } }).ToArray());
        var sink = new AliasByteSink();
        AliasGridFormat.Encode("x", provider, sink, built);
        var payload = sink.Segment(0).ToArray();

        Assert.IsFalse(new AliasGridFormat.Index().TryBind(payload, new AliasReadingTable()), "no table to resolve against");
        Assert.IsFalse(new AliasGridFormat.Index().TryBind(payload), "a table-backed payload cannot bind without one");
        Assert.IsFalse(new AliasGridFormat.Index().TryBind(payload, BindTable(BuildTable(["a"]))), "a table too short for this id");
        Assert.IsTrue(new AliasGridFormat.Index().TryBind(payload, BindTable(built)), "the table it was written against resolves it");
    }

    [TestMethod]
    public void Build_SameReadings_SameIdsAndSameBytes()
    {
        var first = BindTable(BuildTable(["zhong", "chong", "bei"]));
        var second = BindTable(BuildTable(["bei", "zhong", "chong"]));

        CollectionAssert.AreEqual(
            new[] { "bei", "chong", "zhong" },
            new[] { first.Reading(0).ToString(), first.Reading(1).ToString(), first.Reading(2).ToString() },
            "ids are assigned by ordinal sort, so collection order cannot leak into the snapshot's bytes");
        for (var i = 0; i < first.Count; i++)
            Assert.AreEqual(first.Reading(i).ToString(), second.Reading(i).ToString());
    }

    [TestMethod]
    public void Bind_CorruptTablePayload_Refuses()
    {
        var table = BuildTable(["chong", "zhong"]);
        var sink = new AliasByteSink();
        table.WritePayload(sink);
        var payload = sink.Segment(0).ToArray();

        // A width flag that is neither 1 nor 0, a truncated entry, and a trailing byte all mean corruption.
        var badFlag = payload.ToArray();
        badFlag[1] = 7;
        Assert.IsFalse(new AliasReadingTable().TryBind(badFlag));
        Assert.IsFalse(new AliasReadingTable().TryBind(payload.AsSpan(0, payload.Length - 1)));
        Assert.IsFalse(new AliasReadingTable().TryBind(payload.Concat(new byte[] { 0 }).ToArray()));

        // A count no following byte could fill: the bound is what stops a four-byte payload from first
        // becoming two int arrays of a million entries and only then failing.
        Assert.IsFalse(new AliasReadingTable().TryBind([100, 1, 1, (byte)'a']), "100 entries in 4 bytes");
    }
}
