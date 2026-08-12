using Lertaro.Core.IndexV2.Space;

namespace Lertaro.Core.Tests.IndexV2.Space;

[TestClass]
public sealed class LiveSpaceQueryTests
{
    [TestMethod]
    public void GetEntries_UsesRecursiveLogicalSizes()
    {
        using var fixture = BuildSample();

        var root = LiveSpaceQuery.GetEntries(fixture.Index, null).Entries.Single();
        var projects = LiveSpaceQuery.GetEntries(fixture.Index, @"C:\Projects").Entries;

        Assert.AreEqual(400, root.Size);
        Assert.AreEqual(300, projects[0].Size);
        Assert.AreEqual(100, projects[1].Size);
    }

    [TestMethod]
    public void GetEntries_ReflectsUncompactedAddDeleteAndMetadataChanges()
    {
        using var fixture = BuildSample();
        fixture.Index.Mutate((_, delta) =>
        {
            delta.Remove(5);
            delta.Upsert(6, 2, "new.bin", FileRecordFlags.None, 700, 0, 0, 0);
            Core.IndexV2.Delta.DeltaLinkOps.UpdateMetadata(delta, 4, 500, 0, 0, 0);
        });

        var root = LiveSpaceQuery.GetEntries(fixture.Index, null).Entries.Single();
        var projects = LiveSpaceQuery.GetEntries(fixture.Index, @"C:\Projects").Entries;

        Assert.AreEqual(1200, root.Size);
        CollectionAssert.AreEqual(new[] { "new.bin", "App" }, projects.Select(entry => entry.Name).ToArray());
        Assert.AreEqual(700, projects[0].Size);
        Assert.AreEqual(500, projects[1].Size);
    }

    [TestMethod]
    public void GetEntries_DoesNotReopenSnapshotFile()
    {
        using var fixture = BuildSample();
        File.Delete(fixture.Path);
        fixture.Index.Mutate((_, delta) =>
            delta.Upsert(6, 2, "live-only.bin", FileRecordFlags.None, 700, 0, 0, 0));

        var entries = LiveSpaceQuery.GetEntries(fixture.Index, @"C:\Projects").Entries;

        Assert.IsTrue(entries.Any(entry => entry.Name == "live-only.bin" && entry.Size == 700));
    }

    [TestMethod]
    public void GetEntries_CountsHardLinkedFileOnlyOnce()
    {
        using var fixture = LiveIndexFixture.Build("D", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "One", FileRecordFlags.Directory),
            new FileRecord(3, 1, "Two", FileRecordFlags.Directory),
            new FileRecord(4, 2, "shared.bin", FileRecordFlags.None, 512),
            new FileRecord(4, 3, "shared-link.bin", FileRecordFlags.None, 512),
        });

        var root = LiveSpaceQuery.GetEntries(fixture.Index, null).Entries.Single();
        var links = LiveSpaceQuery.GetEntries(fixture.Index, @"D:\One").Entries
            .Concat(LiveSpaceQuery.GetEntries(fixture.Index, @"D:\Two").Entries).ToList();

        Assert.AreEqual(512, root.Size);
        Assert.HasCount(1, links.Where(entry => entry.IsHardLinkDuplicate));
    }

    [TestMethod]
    public void GetEntries_ReattributesSizeWhenCanonicalHardLinkIsDeleted()
    {
        using var fixture = LiveIndexFixture.Build("D", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "One", FileRecordFlags.Directory),
            new FileRecord(3, 1, "Two", FileRecordFlags.Directory),
            new FileRecord(4, 2, "shared.bin", FileRecordFlags.None, 512),
            new FileRecord(4, 3, "shared-link.bin", FileRecordFlags.None, 512),
        });
        fixture.Index.Mutate((_, delta) =>
            Core.IndexV2.Delta.DeltaLinkOps.RemoveLink(delta, 4, 2, "shared.bin"));

        var directories = LiveSpaceQuery.GetEntries(fixture.Index, @"D:\").Entries;

        Assert.AreEqual(512, directories.Sum(entry => entry.Size));
        Assert.AreEqual(512, directories.Single(entry => entry.Name == "Two").Size);
    }

    [TestMethod]
    public void GetEntries_ShowsHiddenEntriesButHidesSystemEntries()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "visible.txt", FileRecordFlags.None, 100),
            new FileRecord(3, 1, "hidden.txt", FileRecordFlags.Hidden, 200),
            new FileRecord(4, 1, "hidden-folder", FileRecordFlags.Directory | FileRecordFlags.Hidden),
            new FileRecord(5, 1, "system.txt", FileRecordFlags.System, 300),
            new FileRecord(6, 1, "system-folder", FileRecordFlags.Directory | FileRecordFlags.System),
        });

        var children = LiveSpaceQuery.GetEntries(fixture.Index, @"C:\").Entries;

        CollectionAssert.AreEquivalent(
            new[] { "visible.txt", "hidden.txt", "hidden-folder" },
            children.Select(entry => entry.Name).ToArray());
    }

    private static LiveIndexFixture BuildSample() => LiveIndexFixture.Build("C", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "Projects", FileRecordFlags.Directory),
        new FileRecord(3, 2, "App", FileRecordFlags.Directory),
        new FileRecord(4, 3, "app.exe", FileRecordFlags.None, 300),
        new FileRecord(5, 2, "readme.md", FileRecordFlags.None, 100),
    });
}
