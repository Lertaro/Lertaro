using Lertaro.Core.IndexV2.Alias;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.Tests.IndexV2.Alias;

// The snapshot's AliasReadings section. Nothing builds a vocabulary any more (it existed so a baked
// reading grid could reference readings by id, and the grid is gone), so what is left to cover is the
// two things the format still needs: the payload the writer emits -- the empty table -- binds back, and
// a NON-EMPTY one from a snapshot already on disk binds too, or that drive would fail to open. Both the
// ASCII and the UTF-16 entry shapes come through here, plus the corruption guards that have to refuse a
// payload rather than read past it.
[TestClass]
public sealed class AliasReadingTableTests
{
    // What the current writer can still produce: one varint, zero entries.
    private static byte[] EmptyPayload()
    {
        var sink = new AliasByteSink();
        AliasReadingTable.Empty.WritePayload(sink);
        Assert.AreEqual(1, sink.SegmentCount, "an empty table is still a section, so it is still a segment");
        return sink.Segment(0).ToArray();
    }

    // A vocabulary a version 12 snapshot can hold on disk: "chong" as ASCII units, 中 as UTF-16.
    private static byte[] LegacyPayload() =>
    [
        2, 1, 5, (byte)'c', (byte)'h', (byte)'o', (byte)'n', (byte)'g',
        0, 1, 0x2D, 0x4E,
    ];

    [TestMethod]
    public void Empty_HasNoEntries()
    {
        Assert.AreEqual(0, AliasReadingTable.Empty.Count);
    }

    [TestMethod]
    public void WriterPayload_BindsBackAsAnEmptyTable()
    {
        var payload = EmptyPayload();

        Assert.AreEqual(1, payload.Length, "a zero entry count is one byte");

        var table = new AliasReadingTable();
        Assert.IsTrue(table.TryBind(payload), "the payload this writer emits must bind");
        Assert.AreEqual(0, table.Count);
    }

    [TestMethod]
    public void TryBind_LegacyVocabulary_ReadsEveryReading()
    {
        var table = new AliasReadingTable();

        Assert.IsTrue(table.TryBind(LegacyPayload()));
        Assert.AreEqual(2, table.Count);
        Assert.AreEqual("chong", table.Reading(0).ToString(), "the one-byte-per-character shape");
        Assert.AreEqual("中", table.Reading(1).ToString(), "the UTF-16 shape");
    }

    [TestMethod]
    public void TryBind_NoPayload_Refuses()
    {
        Assert.IsFalse(new AliasReadingTable().TryBind([]), "not even an entry count is there");
    }

    [TestMethod]
    public void TryBind_CorruptTablePayload_Refuses()
    {
        var payload = LegacyPayload();

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
