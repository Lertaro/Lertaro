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

    private static Func<ISearchResult, bool> GetPredicate(string id)
    {
        var provider = new TypeFilterProvider();
        var group = provider.GetFilterGroups().Single();
        return group.Items.Single(i => i.Id == id).MatchPredicate;
    }

    [TestMethod]
    public void TypeFolder_Directory_IsIncluded() => Assert.IsTrue(GetPredicate("Type_Folder")(new FakeResult { IsDir = true }));

    [TestMethod]
    public void TypeFolder_ApplicationFlaggedDirectory_IsExcluded() => Assert.IsFalse(GetPredicate("Type_Folder")(new FakeResult { IsDir = true, IsApplication = true }));

    [TestMethod]
    public void TypeFile_RegularFile_IsIncluded() => Assert.IsTrue(GetPredicate("Type_File")(new FakeResult { IsDir = false }));

    [TestMethod]
    public void TypeDoc_TxtExtension_IsIncluded() => Assert.IsTrue(GetPredicate("Type_Doc")(new FakeResult { FullPath = @"C:\a.txt" }));

    [TestMethod]
    public void TypeDoc_ExtensionMatchIsCaseInsensitive() => Assert.IsTrue(GetPredicate("Type_Doc")(new FakeResult { FullPath = @"C:\a.TXT" }));

    [TestMethod]
    public void TypeImage_JpgExtension_IsIncluded() => Assert.IsTrue(GetPredicate("Type_Image")(new FakeResult { FullPath = @"C:\photo.jpg" }));

    [TestMethod]
    public void TypeImage_TxtExtension_IsExcluded() => Assert.IsFalse(GetPredicate("Type_Image")(new FakeResult { FullPath = @"C:\a.txt" }));

    [TestMethod]
    public void TypeVideo_Mp4Extension_IsIncluded() => Assert.IsTrue(GetPredicate("Type_Video")(new FakeResult { FullPath = @"C:\clip.mp4" }));

    [TestMethod]
    public void GetFilterGroups_ReturnsAllFiveTypeCategories()
    {
        var provider = new TypeFilterProvider();

        var ids = provider.GetFilterGroups().Single().Items.Select(i => i.Id).ToList();

        CollectionAssert.AreEquivalent(new[] { "Type_Folder", "Type_File", "Type_Doc", "Type_Image", "Type_Video" }, ids);
    }
}
