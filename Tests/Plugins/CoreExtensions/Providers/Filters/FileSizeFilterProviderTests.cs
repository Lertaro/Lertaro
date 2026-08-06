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

    private static Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>> GetPredicate(string id)
    {
        var provider = new FileSizeFilterProvider();
        var group = provider.GetFilterGroups().Single();
        return group.Items.Single(i => i.Id == id).FilterPredicate!;
    }

    private static FakeResult WithSize(long size, bool isDir = false) =>
        new() { IsDir = isDir, Metadata = new FileMetadata(size, default, DateTime.Now, default) };

    [TestMethod]
    public async Task Small_FileUnderOneMb_IsIncluded()
    {
        var filtered = await GetPredicate("Size_Small")(new ISearchResult[] { WithSize(500_000) });

        Assert.HasCount(1, filtered);
    }

    [TestMethod]
    public async Task Small_FileOverOneMb_IsExcluded()
    {
        var filtered = await GetPredicate("Size_Small")(new ISearchResult[] { WithSize(2 * 1024 * 1024) });

        Assert.IsEmpty(filtered);
    }

    [TestMethod]
    public async Task Medium_FileBetweenOneAndHundredMb_IsIncluded()
    {
        var filtered = await GetPredicate("Size_Medium")(new ISearchResult[] { WithSize(50 * 1024 * 1024) });

        Assert.HasCount(1, filtered);
    }

    [TestMethod]
    public async Task Large_FileOverHundredMb_IsIncluded()
    {
        var filtered = await GetPredicate("Size_Large")(new ISearchResult[] { WithSize(200 * 1024 * 1024) });

        Assert.HasCount(1, filtered);
    }

    [TestMethod]
    public async Task Large_Directory_IsExcludedEvenIfMetadataPresent()
    {
        var filtered = await GetPredicate("Size_Large")(new ISearchResult[] { WithSize(200 * 1024 * 1024, isDir: true) });

        Assert.IsEmpty(filtered);
    }

    [TestMethod]
    public async Task Small_UnknownMetadata_IsExcluded()
    {
        var result = new FakeResult { Metadata = new FileMetadata(500, default, DateTime.MinValue, default) };

        var filtered = await GetPredicate("Size_Small")(new ISearchResult[] { result });

        Assert.IsEmpty(filtered);
    }
}
