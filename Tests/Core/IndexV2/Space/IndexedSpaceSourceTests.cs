using Lertaro.Core.IndexV2.Space;

namespace Lertaro.Core.Tests.IndexV2.Space;

[TestClass]
public sealed class IndexedSpaceSourceTests
{
    [TestMethod]
    public void GetChildren_UsesRecursiveLogicalSizes()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "Projects", FileRecordFlags.Directory),
            new FileRecord(3, 2, "App", FileRecordFlags.Directory),
            new FileRecord(4, 3, "app.exe", FileRecordFlags.None, 300),
            new FileRecord(5, 2, "readme.md", FileRecordFlags.None, 100),
        });
        using var source = IndexedSpaceSource.Open(fixture.Path);

        var rootChild = source.GetChildren(source.RootRow).Single();
        var projectChildren = source.GetChildren(rootChild.Row);

        Assert.AreEqual(400, source.TotalSize);
        Assert.AreEqual(400, rootChild.Size);
        Assert.AreEqual(300, projectChildren[0].Size);
        Assert.AreEqual(100, projectChildren[1].Size);
    }

    [TestMethod]
    public void RecursiveSizes_CountHardLinkedFileOnlyOnce()
    {
        using var fixture = LiveIndexFixture.Build("D", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "One", FileRecordFlags.Directory),
            new FileRecord(3, 1, "Two", FileRecordFlags.Directory),
            new FileRecord(4, 2, "shared.bin", FileRecordFlags.None, 512),
            new FileRecord(4, 3, "shared-link.bin", FileRecordFlags.None, 512),
        });
        using var source = IndexedSpaceSource.Open(fixture.Path);

        var directories = source.GetChildren(source.RootRow);
        var linkedEntries = directories.SelectMany(item => source.GetChildren(item.Row)).ToList();

        Assert.AreEqual(512, source.TotalSize);
        Assert.AreEqual(512, directories.Sum(item => item.Size));
        Assert.HasCount(1, linkedEntries.Where(item => item.IsHardLinkDuplicate));
    }

    [TestMethod]
    public void GetChildren_HidesHiddenAndSystemEntries()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "visible.txt", FileRecordFlags.None, 100),
            new FileRecord(3, 1, "hidden.txt", FileRecordFlags.Hidden, 200),
            new FileRecord(4, 1, "system", FileRecordFlags.Directory | FileRecordFlags.System),
        });
        using var source = IndexedSpaceSource.Open(fixture.Path);

        var children = source.GetChildren(source.RootRow);

        Assert.HasCount(1, children);
        Assert.AreEqual("visible.txt", children[0].Name);
    }
}
