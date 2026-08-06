namespace Lertaro.Core.Tests.IndexV2.Delta;

[TestClass]
public sealed class DeltaOverlayTests
{
    private static LiveIndexFixture BuildSampleDrive() => LiveIndexFixture.Build("C", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "Projects", FileRecordFlags.Directory),
        new FileRecord(3, 2, "readme.txt", FileRecordFlags.None),
        new FileRecord(4, 2, "sub", FileRecordFlags.Directory),
        new FileRecord(5, 4, "deep.txt", FileRecordFlags.None),
    });

    // Name search skips IsSuperseded's three hash lookups per row when this holds, and a broad query
    // reaches tens of thousands of rows -- so it has to be false the moment any of the three is
    // populated, or those rows silently stay visible after being deleted, renamed away or overridden.
    [TestMethod]
    public void HasNoBaseChanges_IsTrueOnlyWhileNoBaseRowHasBeenTouched()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            Assert.IsTrue(delta.HasNoBaseChanges, "a freshly opened snapshot has no base changes");

            // An added row is not a base change: it lives past the snapshot's rows and is matched
            // separately, so the fanout's fast path over base rows stays valid.
            delta.Upsert(100, 2, "new.txt", FileRecordFlags.None, 10, 0, 0, 0);
            Assert.IsTrue(delta.HasNoBaseChanges, "an added row does not supersede any base row");

            delta.Remove(3);
            Assert.IsFalse(delta.HasNoBaseChanges, "a deleted base row supersedes");
        });
    }

    [TestMethod]
    public void HasNoBaseChanges_IsFalseAfterABaseRowIsOverridden()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            delta.Upsert(3, 2, "renamed.txt", FileRecordFlags.None, 10, 0, 0, 0);

            Assert.IsFalse(delta.HasNoBaseChanges);
        });
    }

    [TestMethod]
    public void Upsert_NewId_AddsAsAddedRecordAndCountsAsAFile()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            delta.Upsert(100, 2, "new.txt", FileRecordFlags.None, 10, 0, 0, 0);

            Assert.IsTrue(delta.Exists(100));
            Assert.HasCount(1, delta.Added);
            Assert.AreEqual(1, delta.FileCountDelta);
            Assert.AreEqual(0, delta.DirCountDelta);
        });
    }

    [TestMethod]
    public void Upsert_ExistingBaseRow_CreatesOverrideInstead()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            var baseRow = snapshot.FirstRowForId(3);

            delta.Upsert(3, 2, "renamed.txt", FileRecordFlags.None, 20, 0, 0, 0);

            Assert.IsEmpty(delta.Added);
            Assert.IsTrue(delta.BaseOverrides.ContainsKey(baseRow));
            Assert.AreEqual("renamed.txt", delta.NameOf(baseRow));
        });
    }

    [TestMethod]
    public void Upsert_RenamedBaseRow_ChangesReportedFullPath()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            var baseRow = snapshot.FirstRowForId(3);

            delta.Upsert(3, 2, "renamed.txt", FileRecordFlags.None, 20, 0, 0, 0);

            Assert.AreEqual(@"C:\Projects\renamed.txt", delta.GetFullPath(baseRow));
        });
    }

    [TestMethod]
    public void Remove_ExistingBaseRow_MarksVisiblyDeleted()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            var baseRow = snapshot.FirstRowForId(3);

            delta.Remove(3);

            Assert.IsTrue(delta.IsVisiblyDeleted(baseRow));
            Assert.IsFalse(delta.Exists(3));
        });
    }

    [TestMethod]
    public void Remove_Directory_CascadesTombstoneToDescendants()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            var sub = snapshot.FirstRowForId(4);
            var deep = snapshot.FirstRowForId(5);

            delta.Remove(4); // "sub" directory, which contains "deep.txt"

            Assert.IsTrue(delta.IsVisiblyDeleted(sub));
            Assert.IsTrue(delta.IsVisiblyDeleted(deep));
        });
    }

    [TestMethod]
    public void Remove_Directory_DoesNotAffectSiblingSubtree()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            var readme = snapshot.FirstRowForId(3);
            var deep = snapshot.FirstRowForId(5);

            delta.Remove(4); // "sub", a sibling of "readme.txt" under "Projects"

            Assert.IsFalse(delta.IsVisiblyDeleted(readme));
            Assert.IsTrue(delta.IsVisiblyDeleted(deep));
        });
    }

    [TestMethod]
    public void AddedThenRemoved_NoLongerExists()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            delta.Upsert(100, 2, "new.txt", FileRecordFlags.None, 1, 0, 0, 0);
            delta.Remove(100);

            Assert.IsFalse(delta.Exists(100));
        });
    }

    [TestMethod]
    public void PendingChangeCount_ReflectsEveryKindOfChange()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            Assert.AreEqual(0, delta.PendingChangeCount);

            delta.Upsert(100, 2, "new.txt", FileRecordFlags.None, 1, 0, 0, 0); // Added
            delta.Remove(3); // DeletedBase

            Assert.AreEqual(2, delta.PendingChangeCount);
        });
    }

    [TestMethod]
    public void MetadataOf_Override_ReturnsOverriddenValues()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            var baseRow = snapshot.FirstRowForId(3);

            delta.Upsert(3, 2, "readme.txt", FileRecordFlags.None, 999, 1, 2, 3);
            var (size, creation, lastWrite, lastAccess) = delta.MetadataOf(baseRow);

            Assert.AreEqual(999, size);
            Assert.AreEqual(1u, creation);
            Assert.AreEqual(2u, lastWrite);
            Assert.AreEqual(3u, lastAccess);
        });
    }

    [TestMethod]
    public void IsSuperseded_OverriddenBaseRow_IsTrue()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            var baseRow = snapshot.FirstRowForId(3);
            delta.Upsert(3, 2, "renamed.txt", FileRecordFlags.None, 1, 0, 0, 0);

            Assert.IsTrue(delta.IsSuperseded(baseRow));
            // Overridden (still live under a new name), not "visibly deleted".
            Assert.IsFalse(delta.IsVisiblyDeleted(baseRow));
        });
    }
}
