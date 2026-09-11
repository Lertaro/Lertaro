using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Tests.IndexV2;

namespace Lertaro.Core.Tests.Indexer.Usn;

[TestClass]
public sealed class UsnIndexerChangedDirectoriesTests
{
    [TestMethod]
    public void Resolve_ResolvesMoreThan64DistinctParents()
    {
        var records = Enumerable.Range(2, 65)
            .Select(id => new FileRecord((UInt128)id, 1, $"directory-{id}", FileRecordFlags.Directory))
            .Prepend(LiveIndexFixture.Root());
        using var fixture = LiveIndexFixture.Build("C", records);
        var parentFrns = Enumerable.Range(2, 65).Select(id => (UInt128)id).ToHashSet();

        var resolved = UsnIndexerChangedDirectories.Resolve(fixture.Index, parentFrns);

        Assert.IsNotNull(resolved);
        Assert.HasCount(65, resolved);
        CollectionAssert.Contains(resolved!, @"C:\directory-2");
        CollectionAssert.Contains(resolved!, @"C:\directory-66");
    }
}
