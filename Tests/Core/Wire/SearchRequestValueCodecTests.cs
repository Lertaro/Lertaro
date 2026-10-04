using Lertaro.Core.Wire;

namespace Lertaro.Core.Tests.Wire;

// The SetMachineSettings payload: the drive selection followed by the three walk exclusion rule lists.
// The rules ride this request so the service can filter a local drive walk without the App writing them
// into a machine-level file -- see UsnIndexer.WalkOptions. Split from
// SearchRequestBinarySerializerTests to keep that file under the repo's per-file line limit; these cover
// one request's own payload rather than the dispatcher's shared round-trip helper.
[TestClass]
public sealed class SearchRequestValueCodecTests
{
    private static async Task<SearchRequestMessage> RoundTripAsync(SearchRequestMessage message)
    {
        using var stream = new MemoryStream();
        await SearchRequestBinarySerializer.WriteSearchRequestAsync(stream, message);
        stream.Position = 0;
        return await SearchRequestBinarySerializer.ReadSearchRequestAsync(stream);
    }

    [TestMethod]
    public async Task RoundTrip_SetMachineSettings_PreservesLocalDrives()
    {
        var settings = new MachineSettings { LocalDrives = { "C", "D", "Z" } };
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.SetMachineSettings,
            MachineSettings = settings
        });

        CollectionAssert.AreEqual(new[] { "C", "D", "Z" }, result.MachineSettings!.LocalDrives);
    }

    [TestMethod]
    public async Task RoundTrip_SetMachineSettings_EmptyDriveList()
    {
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.SetMachineSettings,
            MachineSettings = new MachineSettings()
        });

        Assert.IsEmpty(result.MachineSettings!.LocalDrives);
    }

    // All three rule lists travel alongside the drive selection they share the message with. A regression
    // here would silently leave the local drive walk filtering by nothing.
    [TestMethod]
    public async Task RoundTrip_SetMachineSettings_PreservesTheThreeExclusionRuleLists()
    {
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.SetMachineSettings,
            MachineSettings = new MachineSettings { LocalDrives = { "C" } },
            ExcludedPaths = [@"C:\excluded", @"D:\also"],
            IgnoredPathGlobs = ["node_modules", "*.tmp"],
            IgnoredPathRegexes = ["^secret-"]
        });

        CollectionAssert.AreEqual(new[] { "C" }, result.MachineSettings!.LocalDrives);
        CollectionAssert.AreEqual(new[] { @"C:\excluded", @"D:\also" }, result.ExcludedPaths);
        CollectionAssert.AreEqual(new[] { "node_modules", "*.tmp" }, result.IgnoredPathGlobs);
        CollectionAssert.AreEqual(new[] { "^secret-" }, result.IgnoredPathRegexes);
    }

    // A request carrying no rules at all -- the shape an older App sends, and the drive-selection-only
    // update the settings page issues -- has to stay distinguishable from one carrying empty lists. The
    // service reads null as "no change" and an empty list as "the user has no rules", so collapsing the
    // two would make a drive-selection update wipe rules it never mentioned.
    [TestMethod]
    public async Task RoundTrip_SetMachineSettings_WithoutRuleLists_LeavesThemNull()
    {
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.SetMachineSettings,
            MachineSettings = new MachineSettings { LocalDrives = { "C" } }
        });

        Assert.IsNull(result.ExcludedPaths);
        Assert.IsNull(result.IgnoredPathGlobs);
        Assert.IsNull(result.IgnoredPathRegexes);
    }

    // The other side of that distinction: lists present but empty come back empty, not null, or "the user
    // cleared every rule" would read as "no change" and the service would keep filtering by rules the user
    // just deleted.
    [TestMethod]
    public async Task RoundTrip_SetMachineSettings_WithEmptyRuleLists_KeepsThemEmptyNotNull()
    {
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.SetMachineSettings,
            MachineSettings = new MachineSettings(),
            ExcludedPaths = [],
            IgnoredPathGlobs = [],
            IgnoredPathRegexes = []
        });

        Assert.IsNotNull(result.ExcludedPaths);
        Assert.IsNotNull(result.IgnoredPathGlobs);
        Assert.IsNotNull(result.IgnoredPathRegexes);
        Assert.IsEmpty(result.ExcludedPaths);
        Assert.IsEmpty(result.IgnoredPathGlobs);
        Assert.IsEmpty(result.IgnoredPathRegexes);
    }

    // Forward compatibility, and the reason the three lists were appended rather than woven into the drive
    // block: a payload written before this field existed ends right after the drives. It must still parse
    // -- as "no rules sent" -- instead of reading past the end of the buffer.
    [TestMethod]
    public async Task ReadSearchRequest_DrivesOnlyPayloadFromAnOlderApp_ParsesWithNullRules()
    {
        var msg = new SearchRequestMessage
        {
            Id = SearchRequestId.SetMachineSettings,
            MachineSettings = new MachineSettings { LocalDrives = { "C" } }
        };
        using var stream = new MemoryStream();
        await SearchRequestBinarySerializer.WriteSearchRequestAsync(stream, msg);

        // Rewrite the payload length to drop the three trailing lists, reproducing an older writer's bytes.
        var bytes = stream.ToArray();
        var driveBlockEnd = 12 + 1 + sizeof(int) + 1 + 1; // header + id + count + "C" (len byte + 1 char)
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(8), driveBlockEnd - 12);
        var truncated = new MemoryStream(bytes, 0, driveBlockEnd);

        var result = await SearchRequestBinarySerializer.ReadSearchRequestAsync(truncated);

        CollectionAssert.AreEqual(new[] { "C" }, result.MachineSettings!.LocalDrives);
        Assert.IsNull(result.ExcludedPaths);
        Assert.IsNull(result.IgnoredPathGlobs);
        Assert.IsNull(result.IgnoredPathRegexes);
    }
}
