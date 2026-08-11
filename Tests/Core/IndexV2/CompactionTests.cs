using Lertaro.Core.IndexV2;

namespace Lertaro.Core.Tests.IndexV2;

[TestClass]
public sealed class CompactionTests
{
    private static LiveIndexFixture BuildSampleDrive() => LiveIndexFixture.Build("C", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "Projects", FileRecordFlags.Directory),
        new FileRecord(3, 2, "readme.txt", FileRecordFlags.None),
        new FileRecord(4, 2, "notes.md", FileRecordFlags.None),
    });

    [TestMethod]
    public void BuildMergedStore_NoChanges_ReturnsEveryBaseRecordUnchanged()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            var store = Compaction.BuildMergedStore(snapshot, delta);

            Assert.HasCount(4, store.Records);
            CollectionAssert.Contains(store.Records.Select(r => r.Name).ToList(), "readme.txt");
        });
    }

    [TestMethod]
    public void BuildMergedStore_WithOverride_ReflectsRenamedRecord()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            delta.Upsert(3, 2, "renamed.txt", FileRecordFlags.None, 1, 0, 0, 0);

            var store = Compaction.BuildMergedStore(snapshot, delta);

            var names = store.Records.Select(r => r.Name).ToList();
            CollectionAssert.Contains(names, "renamed.txt");
            CollectionAssert.DoesNotContain(names, "readme.txt");
            Assert.HasCount(4, store.Records); // still one row per live id, just patched in place
        });
    }

    [TestMethod]
    public void BuildMergedStore_WithDeletedRow_ExcludesIt()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            delta.Remove(3);

            var store = Compaction.BuildMergedStore(snapshot, delta);

            Assert.HasCount(3, store.Records);
            CollectionAssert.DoesNotContain(store.Records.Select(r => r.Name).ToList(), "readme.txt");
        });
    }

    [TestMethod]
    public void BuildMergedStore_WithAddedRow_IncludesIt()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            delta.Upsert(100, 2, "new.txt", FileRecordFlags.None, 42, 0, 0, 0);

            var store = Compaction.BuildMergedStore(snapshot, delta);

            Assert.HasCount(5, store.Records);
            var added = store.Records.Single(r => r.Name == "new.txt");
            Assert.AreEqual(42, added.Size);
        });
    }

    [TestMethod]
    public void BuildMergedStore_WithAttributeUpdate_PersistsCurrentFlags()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            Core.IndexV2.Delta.DeltaLinkOps.UpdateFlags(delta, 3, FileRecordFlags.Hidden);

            var store = Compaction.BuildMergedStore(snapshot, delta);

            Assert.IsTrue(store.Records.Single(r => r.Name == "readme.txt").Flags.HasFlag(FileRecordFlags.Hidden));
        });
    }

    [TestMethod]
    public void BuildMergedStore_RenamedAwayRowWithNoSuccessor_IsDropped()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            Core.IndexV2.Delta.DeltaLinkOps.RemoveLinkForRename(delta, 3, 2, "readme.txt");

            var store = Compaction.BuildMergedStore(snapshot, delta);

            Assert.HasCount(3, store.Records);
            CollectionAssert.DoesNotContain(store.Records.Select(r => r.Name).ToList(), "readme.txt");
        });
    }

    [TestMethod]
    public void BuildMergedStore_RemovedAddedRecord_IsExcluded()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            delta.Upsert(100, 2, "new.txt", FileRecordFlags.None, 1, 0, 0, 0);
            delta.Remove(100);

            var store = Compaction.BuildMergedStore(snapshot, delta);

            Assert.HasCount(4, store.Records);
        });
    }

    [TestMethod]
    public void BuildMergedStore_Stamp_OverridesJournalIdAndCompletionFlag()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            var stamp = new CompactionStamp(JournalId: 999, NextUsn: 12345, IsComplete: true);

            var store = Compaction.BuildMergedStore(snapshot, delta, stamp);

            Assert.AreEqual(999ul, store.JournalId);
            Assert.AreEqual(12345L, store.NextUsn);
            Assert.IsTrue(store.IsComplete);
        });
    }

    [TestMethod]
    public void BuildMergedStore_StampOmitted_KeepsSnapshotsCurrentValues()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            var store = Compaction.BuildMergedStore(snapshot, delta);

            Assert.AreEqual(snapshot.JournalId, store.JournalId);
            Assert.AreEqual(snapshot.IsComplete, store.IsComplete);
        });
    }
}
