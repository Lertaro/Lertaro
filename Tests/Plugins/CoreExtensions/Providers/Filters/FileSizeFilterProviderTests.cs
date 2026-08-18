using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Providers.Filters;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.Filters;

[TestClass]
public sealed class FileSizeFilterProviderTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
        public FileMetadata Metadata { get; init; }
    }

    private static Func<ISearchResult, bool> GetPredicate(string id)
    {
        var provider = new FileSizeFilterProvider();
        var group = provider.GetFilterGroups().Single();
        return group.Items.Single(i => i.Id == id).MatchPredicate;
    }

    private static FakeResult WithSize(long size, bool isDir = false) =>
        new() { IsDir = isDir, Metadata = new FileMetadata(size, default, DateTime.Now, default) };

    [TestMethod]
    public void Small_FileUnderOneMb_IsIncluded() => Assert.IsTrue(GetPredicate("Size_Small")(WithSize(500_000)));

    [TestMethod]
    public void Small_FileOverOneMb_IsExcluded() => Assert.IsFalse(GetPredicate("Size_Small")(WithSize(2 * 1024 * 1024)));

    [TestMethod]
    public void Medium_FileBetweenOneAndHundredMb_IsIncluded() => Assert.IsTrue(GetPredicate("Size_Medium")(WithSize(50 * 1024 * 1024)));

    [TestMethod]
    public void Large_FileOverHundredMb_IsIncluded() => Assert.IsTrue(GetPredicate("Size_Large")(WithSize(200 * 1024 * 1024)));

    [TestMethod]
    public void Large_Directory_IsExcludedEvenIfMetadataPresent() => Assert.IsFalse(GetPredicate("Size_Large")(WithSize(200 * 1024 * 1024, isDir: true)));

    [TestMethod]
    public void Small_UnknownMetadata_IsExcluded()
    {
        var result = new FakeResult { Metadata = new FileMetadata(500, default, DateTime.MinValue, default) };

        Assert.IsFalse(GetPredicate("Size_Small")(result));
    }
}
