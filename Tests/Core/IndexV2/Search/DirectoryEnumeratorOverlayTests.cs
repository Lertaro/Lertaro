using static Lertaro.Core.Tests.IndexV2.Search.DirectoryEnumeratorFixture;

namespace Lertaro.Core.Tests.IndexV2.Search;

// The same listing, but with a delta overlay on top of the snapshot. A row added, deleted, renamed or
// moved since the snapshot has to be listed exactly once, under the right parent -- the CSR pass and the
// override pass must not both claim it, and neither may miss it.
[TestClass]
public sealed class DirectoryEnumeratorOverlayTests
{
    [TestMethod]
    public void Enumerate_AddedRow_ShowsUpUnderItsParent()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) => delta.Upsert(100, 2, "new.txt", FileRecordFlags.None, 10, 0, 0, 0));

        var (_, results) = Enumerate(fixture, @"C:\Projects");

        CollectionAssert.Contains(Paths(results), @"C:\Projects\new.txt");
        Assert.HasCount(4, results);
    }

    [TestMethod]
    public void Enumerate_DeletedRow_IsGoneFromItsDirectory()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) => delta.Remove(3));

        var (_, results) = Enumerate(fixture, @"C:\Projects");

        CollectionAssert.AreEqual(new[] { @"C:\Projects\notes.md", @"C:\Projects\sub" }, Paths(results));
    }

    // A renamed row keeps its base row (and therefore its children), so it must be listed exactly once:
    // the CSR pass has to skip it and the override pass has to pick it up, not the other way around.
    [TestMethod]
    public void Enumerate_RenamedInPlace_IsListedOnceUnderTheNewName()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) => delta.Upsert(3, 2, "renamed.txt", FileRecordFlags.None, 10, 0, 0, 0));

        var (_, results) = Enumerate(fixture, @"C:\Projects");

        CollectionAssert.AreEqual(
            new[] { @"C:\Projects\notes.md", @"C:\Projects\renamed.txt", @"C:\Projects\sub" },
            Paths(results));
    }

    [TestMethod]
    public void Enumerate_MovedRow_LeavesTheOldDirectoryAndAppearsInTheNewOne()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) => delta.Upsert(3, 7, "readme.txt", FileRecordFlags.None, 10, 0, 0, 0));

        var moved = Enumerate(fixture, @"C:\Downloads").Results;
        var origin = Enumerate(fixture, @"C:\Projects").Results;

        CollectionAssert.AreEqual(new[] { @"C:\Downloads\install.exe", @"C:\Downloads\readme.txt" }, Paths(moved));
        CollectionAssert.DoesNotContain(Paths(origin), @"C:\Projects\readme.txt");
    }

    [TestMethod]
    public void Enumerate_MovedDirectory_TakesItsBaseChildrenWithIt()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) => delta.Upsert(5, 7, "sub", FileRecordFlags.Directory, 0, 0, 0, 0));

        var (_, results) = Enumerate(fixture, @"C:\Downloads", recursive: true);

        CollectionAssert.AreEqual(
            new[] { @"C:\Downloads\install.exe", @"C:\Downloads\sub", @"C:\Downloads\sub\deep.txt" },
            Paths(results));
    }

    // A directory created since the snapshot has no base row at all, so its own children can only be
    // found by FRN -- the case a walk that only ever consults the CSR would come up empty on.
    [TestMethod]
    public void Enumerate_ChildrenOfADirectoryAddedByTheOverlay_AreReachedRecursively()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            delta.Upsert(200, 2, "newdir", FileRecordFlags.Directory, 0, 0, 0, 0);
            delta.Upsert(201, 200, "inner.txt", FileRecordFlags.None, 10, 0, 0, 0);
            delta.Upsert(202, 201, "ignored", FileRecordFlags.None, 10, 0, 0, 0);
        });

        var (_, results) = Enumerate(fixture, @"C:\Projects", recursive: true);

        CollectionAssert.Contains(Paths(results), @"C:\Projects\newdir");
        CollectionAssert.Contains(Paths(results), @"C:\Projects\newdir\inner.txt");
        // Parented to a FILE, so it belongs to no directory and must not surface anywhere.
        CollectionAssert.DoesNotContain(Paths(results), @"C:\Projects\newdir\inner.txt\ignored");
    }

    [TestMethod]
    public void Enumerate_HiddenRowFromTheOverlay_IsFilteredToo()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            delta.Upsert(100, 2, "new.tmp", FileRecordFlags.Hidden, 10, 0, 0, 0);
            // An existing visible row turned hidden in place must stop showing up as well.
            delta.Upsert(4, 2, "notes.md", FileRecordFlags.System, 10, 0, 0, 0);
        });

        var (_, results) = Enumerate(fixture, @"C:\Projects");

        CollectionAssert.AreEqual(new[] { @"C:\Projects\readme.txt", @"C:\Projects\sub" }, Paths(results));
    }

    [TestMethod]
    public void Enumerate_DirectoryDeletedByTheOverlay_ReportsUnresolved()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) => delta.Remove(5));

        var (resolved, results) = Enumerate(fixture, @"C:\Projects\sub");

        Assert.IsFalse(resolved);
        Assert.IsEmpty(results);
    }
}
