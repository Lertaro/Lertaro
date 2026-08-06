using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Providers.Filters;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.Filters;

[TestClass]
public sealed class TypeFilterProviderTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    private static Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>> GetPredicate(string id)
    {
        var provider = new TypeFilterProvider();
        var group = provider.GetFilterGroups().Single();
        return group.Items.Single(i => i.Id == id).FilterPredicate!;
    }

    [TestMethod]
    public async Task TypeFolder_Directory_IsIncluded()
    {
        var filtered = await GetPredicate("Type_Folder")(new ISearchResult[] { new FakeResult { IsDir = true } });

        Assert.HasCount(1, filtered);
    }

    [TestMethod]
    public async Task TypeFolder_ApplicationFlaggedDirectory_IsExcluded()
    {
        var filtered = await GetPredicate("Type_Folder")(new ISearchResult[] { new FakeResult { IsDir = true, IsApplication = true } });

        Assert.IsEmpty(filtered);
    }

    [TestMethod]
    public async Task TypeFile_RegularFile_IsIncluded()
    {
        var filtered = await GetPredicate("Type_File")(new ISearchResult[] { new FakeResult { IsDir = false } });

        Assert.HasCount(1, filtered);
    }

    [TestMethod]
    public async Task TypeDoc_TxtExtension_IsIncluded()
    {
        var filtered = await GetPredicate("Type_Doc")(new ISearchResult[] { new FakeResult { FullPath = @"C:\a.txt" } });

        Assert.HasCount(1, filtered);
    }

    [TestMethod]
    public async Task TypeDoc_ExtensionMatchIsCaseInsensitive()
    {
        var filtered = await GetPredicate("Type_Doc")(new ISearchResult[] { new FakeResult { FullPath = @"C:\a.TXT" } });

        Assert.HasCount(1, filtered);
    }

    [TestMethod]
    public async Task TypeImage_JpgExtension_IsIncluded()
    {
        var filtered = await GetPredicate("Type_Image")(new ISearchResult[] { new FakeResult { FullPath = @"C:\photo.jpg" } });

        Assert.HasCount(1, filtered);
    }

    [TestMethod]
    public async Task TypeImage_TxtExtension_IsExcluded()
    {
        var filtered = await GetPredicate("Type_Image")(new ISearchResult[] { new FakeResult { FullPath = @"C:\a.txt" } });

        Assert.IsEmpty(filtered);
    }

    [TestMethod]
    public async Task TypeVideo_Mp4Extension_IsIncluded()
    {
        var filtered = await GetPredicate("Type_Video")(new ISearchResult[] { new FakeResult { FullPath = @"C:\clip.mp4" } });

        Assert.HasCount(1, filtered);
    }

    [TestMethod]
    public void GetFilterGroups_ReturnsAllFiveTypeCategories()
    {
        var provider = new TypeFilterProvider();

        var ids = provider.GetFilterGroups().Single().Items.Select(i => i.Id).ToList();

        CollectionAssert.AreEquivalent(new[] { "Type_Folder", "Type_File", "Type_Doc", "Type_Image", "Type_Video" }, ids);
    }
}
