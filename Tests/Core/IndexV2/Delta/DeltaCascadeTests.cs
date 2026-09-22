using Lertaro.Core.IndexV2.Delta;

namespace Lertaro.Core.Tests.IndexV2.Delta;

// A delete cascade walks CURRENT parentage, recursing between Tombstone and RemoveAdded. The cycles are
// broken by the Removed / DeletedBase flags set before each step, so what actually reaches the stack is a
// legitimately deep tree -- and every other walk in this module treats depth as a backstop worth capping.
[TestClass]
public sealed class DeltaCascadeTests
{
    private const int Levels = 600;
    private const int MaxDepth = 512;

    [TestMethod]
    public void ADirectoryCascade_DeletingADeepTreeStopsAtTheSameCapAsTheOtherWalks()
    {
        using var fixture = LiveIndexFixture.Build("C", NestedDirectories(Levels));
        var rows = new List<int>();

        fixture.Index.Mutate((_, delta) =>
        {
            DeltaCascade.Tombstone(delta, 1); // row 1 is the first directory under the root
            rows.AddRange(delta.DeletedBase);
        });

        Assert.HasCount(MaxDepth, rows, "the cascade stops at the depth cap instead of following all 600 levels");
        Assert.Contains(1, rows);
        Assert.DoesNotContain(MaxDepth + 1, rows);
    }

    [TestMethod]
    public void ADirectoryCascade_WithinTheCapReachesEveryLevel()
    {
        using var fixture = LiveIndexFixture.Build("C", NestedDirectories(40));
        var rows = new List<int>();

        fixture.Index.Mutate((_, delta) =>
        {
            DeltaCascade.Tombstone(delta, 1);
            rows.AddRange(delta.DeletedBase);
        });

        Assert.HasCount(40, rows, "the cap is a corruption backstop, not a shorter normal limit");
    }

    private static IEnumerable<FileRecord> NestedDirectories(int levels)
    {
        yield return LiveIndexFixture.Root();
        for (var level = 0; level < levels; level++)
            yield return new FileRecord((ulong)(level + 2), (ulong)(level + 1), $"dir{level}", FileRecordFlags.Directory);
    }
}
