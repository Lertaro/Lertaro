using Lertaro.App.Views.SpaceAnalyzer;
using Lertaro.Core.IndexV2.Space;

namespace Lertaro.App.Tests.Views.SpaceAnalyzer;

[TestClass]
public sealed class SpaceAnalyzerLocationResolverTests
{
    [TestMethod]
    public async Task TrimUnavailableAsync_CurrentDirectoryStillIndexed_KeepsHistory()
    {
        var history = History(@"C:\", @"C:\Projects");

        var changed = await SpaceAnalyzerLocationResolver.TrimUnavailableAsync(history,
            (path, _) => Task.FromResult<IReadOnlyList<SpaceIndexEntry>>(
                path == @"C:\" ? [Directory(@"C:\Projects")] : []), CancellationToken.None);

        Assert.IsFalse(changed);
        Assert.HasCount(3, history);
    }

    [TestMethod]
    public async Task TrimUnavailableAsync_RenamedDirectory_ReturnsToIndexedParent()
    {
        var history = History(@"C:\", @"C:\Projects", @"C:\Projects\OldName");

        var changed = await SpaceAnalyzerLocationResolver.TrimUnavailableAsync(history, (path, _) =>
            Task.FromResult<IReadOnlyList<SpaceIndexEntry>>(path switch
            {
                @"C:\Projects" => [Directory(@"C:\Projects\NewName")],
                @"C:\" => [Directory(@"C:\Projects")],
                _ => []
            }), CancellationToken.None);

        Assert.IsTrue(changed);
        Assert.HasCount(3, history);
        Assert.AreEqual(@"C:\Projects", history[^1].Path);
    }

    [TestMethod]
    public async Task TrimUnavailableAsync_RemovedIndex_ReturnsToRoot()
    {
        var history = History(@"C:\", @"C:\Projects");

        var changed = await SpaceAnalyzerLocationResolver.TrimUnavailableAsync(history,
            (_, _) => Task.FromResult<IReadOnlyList<SpaceIndexEntry>>([]), CancellationToken.None);

        Assert.IsTrue(changed);
        Assert.HasCount(1, history);
        Assert.IsNull(history[0].Path);
    }

    private static List<SpaceAnalyzerLocation> History(params string[] paths) =>
        [new(null, "Home"), .. paths.Select(path => new SpaceAnalyzerLocation(path, path))];

    private static SpaceIndexEntry Directory(string path) =>
        new(path, System.IO.Path.GetFileName(path.TrimEnd('\\')), 0, true, false);
}
