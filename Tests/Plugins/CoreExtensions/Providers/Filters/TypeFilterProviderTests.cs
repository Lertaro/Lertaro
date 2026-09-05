using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.CoreExtensions.Models;
using Lertaro.Plugins.CoreExtensions.Providers.Filters;
using Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.Filters;

[TestClass]
[DoNotParallelize]
public sealed class TypeFilterProviderTests
{
    [TestInitialize]
    [TestCleanup]
    public void ResetSettings() => PluginSettingsService.GetSettingFunc = null;

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
    public void GetFilterGroups_DisabledBuiltInFilter_IsOmitted()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) =>
            key == TypeFilterProvider.ImageFilterEnabledKey ? false : fallback;

        var ids = new TypeFilterProvider().GetFilterGroups().Single().Items.Select(i => i.Id).ToList();

        Assert.DoesNotContain("Type_Image", ids);
        Assert.Contains("Type_Doc", ids);
        Assert.Contains("Type_Video", ids);
    }

    [TestMethod]
    public void GetFilterGroups_CustomFilter_ExpandsRuleAndKeepsIcon()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) => key == TypeFilterProvider.SidebarCustomFiltersKey
            ? new List<CustomFilterItem>
            {
                new() { Keyword = "executables", Rule = "*.ps1; *.exe", Icon = "M1 2" }
            }
            : fallback;

        var items = new TypeFilterProvider().GetFilterGroups().Single().Items;
        var item = items.Single(i => i.Id == "Type_Custom_0");

        Assert.AreEqual("executables", item.DisplayName);
        Assert.AreEqual("M1 2", item.IconData);
        Assert.IsTrue(item.MatchPredicate(new FakeResult { Name = "build.ps1" }));
        Assert.IsTrue(item.MatchPredicate(new FakeResult { Name = "tool.exe" }));
        Assert.IsFalse(item.MatchPredicate(new FakeResult { Name = "readme.txt" }));
    }

    [TestMethod]
    public void GetFilterGroups_CustomFilterReference_UsesDisabledQueryFilter()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) => key switch
        {
            TypeFilterProvider.SidebarCustomFiltersKey => new List<CustomFilterItem>
            {
                new() { Keyword = "executables", Rule = "@scripts; *.exe" }
            },
            CustomFilterQueryTokenProvider.SettingKey => new List<CustomFilterItem>
            {
                new() { Enabled = false, Keyword = "scripts", Rule = "*.ps1" }
            },
            _ => fallback
        };

        var item = new TypeFilterProvider().GetFilterGroups().Single().Items.Single(i => i.Id == "Type_Custom_0");

        Assert.IsTrue(item.MatchPredicate(new FakeResult { Name = "build.ps1" }));
        Assert.IsTrue(item.MatchPredicate(new FakeResult { Name = "tool.exe" }));
    }

    [TestMethod]
    public void GetFilterGroups_CustomFilterWithMissingRuleReference_IsOmitted()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) => key == TypeFilterProvider.SidebarCustomFiltersKey
            ? new List<CustomFilterItem> { new() { Keyword = "missing", Rule = "@unknown" } }
            : fallback;

        var ids = new TypeFilterProvider().GetFilterGroups().Single().Items.Select(i => i.Id).ToList();

        Assert.DoesNotContain("Type_Custom_0", ids);
    }

    [TestMethod]
    public void GetFilterGroups_CustomFilterWithEmptyName_IsOmitted()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) => key == TypeFilterProvider.SidebarCustomFiltersKey
            ? new List<CustomFilterItem> { new() { Keyword = "  ", Rule = "*.exe" } }
            : fallback;

        var ids = new TypeFilterProvider().GetFilterGroups().Single().Items.Select(item => item.Id);

        Assert.DoesNotContain("Type_Custom_0", ids);
    }

    [TestMethod]
    public void GetFilterGroups_DisabledSidebarFilter_IsHidden()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) => key == TypeFilterProvider.SidebarCustomFiltersKey
            ? new List<CustomFilterItem> { new() { Enabled = false, Keyword = "scripts", Rule = "*.ps1" } }
            : fallback;

        var ids = new TypeFilterProvider().GetFilterGroups().Single().Items.Select(item => item.Id);

        Assert.DoesNotContain("Type_Custom_0", ids);
    }

    [TestMethod]
    public void GetFilterGroups_ReturnsAllFiveTypeCategories()
    {
        var provider = new TypeFilterProvider();

        var ids = provider.GetFilterGroups().Single().Items.Select(i => i.Id).ToList();

        CollectionAssert.AreEquivalent(new[] { "Type_Folder", "Type_File", "Type_Doc", "Type_Image", "Type_Video" }, ids);
    }
}
