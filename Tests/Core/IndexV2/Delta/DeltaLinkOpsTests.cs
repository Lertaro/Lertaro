using Lertaro.Core.IndexV2.Delta;

namespace Lertaro.Core.Tests.IndexV2.Delta;

[TestClass]
public sealed class DeltaLinkOpsTests
{
    private static LiveIndexFixture BuildSampleDrive() => LiveIndexFixture.Build("C", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "Projects", FileRecordFlags.Directory),
        new FileRecord(3, 2, "readme.txt", FileRecordFlags.None),
    });

    [TestMethod]
    public void AddLink_NewLink_BecomesLive()
    {
        // DeltaOverlay.Exists() only recognizes rows added via Upsert (it consults the private
        // _addedById index, which AddLink/ToggleLink never populate) -- checking delta.Added directly
        // is the correct signal for a record added via DeltaLinkOps.
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            DeltaLinkOps.AddLink(delta, 200, 2, "linked.txt", FileRecordFlags.None);

            Assert.IsTrue(delta.Added.Any(r => r.Id == 200 && !r.Removed));
        });
    }

    [TestMethod]
    public void AddLink_ExactDuplicateLink_IsIgnored()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            DeltaLinkOps.AddLink(delta, 200, 2, "linked.txt", FileRecordFlags.None);
            DeltaLinkOps.AddLink(delta, 200, 2, "linked.txt", FileRecordFlags.None);

            Assert.HasCount(1, delta.Added);
        });
    }

    [TestMethod]
    public void RemoveLink_ExistingBaseLink_TombstonesIt()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            var baseRow = snapshot.FirstRowForId(3);

            DeltaLinkOps.RemoveLink(delta, 3, 2, "readme.txt");

            Assert.IsTrue(delta.IsVisiblyDeleted(baseRow));
        });
    }

    [TestMethod]
    public void RemoveLinkForRename_ExistingBaseLink_MarksRenamedAwayNotHardDeleted()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            var baseRow = snapshot.FirstRowForId(3);

            DeltaLinkOps.RemoveLinkForRename(delta, 3, 2, "readme.txt");

            Assert.IsTrue(delta.RenamedAway.ContainsKey(baseRow));
            Assert.DoesNotContain(baseRow, delta.DeletedBase);
            Assert.IsTrue(delta.IsVisiblyDeleted(baseRow)); // gone under its OLD identity either way
        });
    }

    [TestMethod]
    public void ToggleLink_LinkNotPresent_AddsIt()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            DeltaLinkOps.ToggleLink(delta, 200, 2, "toggled.txt", FileRecordFlags.None);

            Assert.IsTrue(delta.Added.Any(r => r.Id == 200 && !r.Removed));
        });
    }

    [TestMethod]
    public void ToggleLink_LinkAlreadyAdded_RemovesIt()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            DeltaLinkOps.AddLink(delta, 200, 2, "toggled.txt", FileRecordFlags.None);

            DeltaLinkOps.ToggleLink(delta, 200, 2, "toggled.txt", FileRecordFlags.None);

            Assert.IsTrue(delta.Added.Single(r => r.Id == 200).Removed);
        });
    }

    [TestMethod]
    public void ToggleLink_ExistingBaseLink_TombstonesIt()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            var baseRow = snapshot.FirstRowForId(3);

            DeltaLinkOps.ToggleLink(delta, 3, 2, "readme.txt", FileRecordFlags.None);

            Assert.IsTrue(delta.IsVisiblyDeleted(baseRow));
        });
    }

    [TestMethod]
    public void UpdateMetadata_ExistingBaseRow_OverlaysNewValues()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            var baseRow = snapshot.FirstRowForId(3);

            DeltaLinkOps.UpdateMetadata(delta, 3, 555, 10, 20, 30);
            var (size, creation, lastWrite, lastAccess) = delta.MetadataOf(baseRow);

            Assert.AreEqual(555, size);
            Assert.AreEqual(10u, creation);
            Assert.AreEqual(20u, lastWrite);
            Assert.AreEqual(30u, lastAccess);
        });
    }

    [TestMethod]
    public void UpdateMetadata_AddedRecord_PatchesItInPlace()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            DeltaLinkOps.AddLink(delta, 200, 2, "new.txt", FileRecordFlags.None);

            DeltaLinkOps.UpdateMetadata(delta, 200, 777, 1, 2, 3);

            var record = delta.Added.First(r => r.Id == 200);
            Assert.AreEqual(777, record.Size);
            Assert.AreEqual(1u, record.Creation);
        });
    }

    [TestMethod]
    public void UpdateFlags_ExistingBaseRow_OverridesAttributesAndPreservesMetadata()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            var baseRow = snapshot.FirstRowForId(3);
            DeltaLinkOps.UpdateMetadata(delta, 3, 555, 10, 20, 30);

            DeltaLinkOps.UpdateFlags(delta, 3, FileRecordFlags.Hidden | FileRecordFlags.ReadOnly);

            var record = delta.BaseOverrides[baseRow];
            Assert.AreEqual((ushort)(FileRecordFlags.Hidden | FileRecordFlags.ReadOnly), record.Flags);
            Assert.AreEqual(555, record.Size);
            Assert.AreEqual(20u, record.LastWrite);
        });
    }

    [TestMethod]
    public void UpdateFlags_AddedRecord_PatchesItInPlace()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            DeltaLinkOps.AddLink(delta, 200, 2, "new.txt", FileRecordFlags.None);

            DeltaLinkOps.UpdateFlags(delta, 200, FileRecordFlags.System);

            Assert.AreEqual((ushort)FileRecordFlags.System, delta.Added.Single(r => r.Id == 200).Flags);
        });
    }
}
