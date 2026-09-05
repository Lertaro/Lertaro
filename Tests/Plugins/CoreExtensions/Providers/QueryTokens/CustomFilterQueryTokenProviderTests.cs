using Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;
using Lertaro.Plugins.CoreExtensions.Models;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.QueryTokens;

[TestClass]
[DoNotParallelize]
public class CustomFilterQueryTokenProviderTests
{
    [TestInitialize]
    [TestCleanup]
    public void Reset() => PluginSettingsService.GetSettingFunc = null;

    private sealed class FakeSearchResult : ISearchResult
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string ContextDirectory { get; set; } = string.Empty;
        public bool IsDir { get; set; }
        public bool IsApplication { get; set; }
        public string ResultKind { get; set; } = "File";
        public FileMetadata Metadata { get; set; }
        public Action? OnExecute { get; set; }
    }

    [TestMethod]
    public void CanHandle_TokenStartsWithAt_ReturnsTrue()
    {
        var provider = new CustomFilterQueryTokenProvider();
        Assert.IsTrue(provider.CanHandle("@doc"));
        Assert.IsTrue(provider.CanHandle("@video"));
        Assert.IsFalse(provider.CanHandle("@"));
        Assert.IsFalse(provider.CanHandle("doc"));
        Assert.IsFalse(provider.CanHandle(".doc"));
    }

    [TestMethod]
    public void CanHandle_CustomPrefix_ReturnsTrue()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) => key == CustomFilterQueryTokenProvider.PrefixSettingKey ? "!" : fallback;
        var provider = new CustomFilterQueryTokenProvider();
        Assert.IsTrue(provider.CanHandle("!doc"));
        Assert.IsFalse(provider.CanHandle("@doc"));
    }

    [TestMethod]
    public void ApplyRule_WildcardAndExtensions_FiltersMatchingResultsCorrectly()
    {
        var results = new List<ISearchResult>
        {
            new FakeSearchResult { Name = "report.docx", FullPath = @"C:\docs\report.docx", IsDir = false },
            new FakeSearchResult { Name = "photo.jpg", FullPath = @"C:\pics\photo.jpg", IsDir = false },
            new FakeSearchResult { Name = "archive.tar.gz", FullPath = @"C:\zips\archive.tar.gz", IsDir = false },
            new FakeSearchResult { Name = "subfolder", FullPath = @"C:\docs\subfolder", IsDir = true },
        };

        var filteredDoc = CustomFilterQueryTokenProvider.ApplyRule("*.doc; *.docx; *.pdf", results);
        Assert.HasCount(1, filteredDoc);
        Assert.AreEqual("report.docx", filteredDoc[0].Name);

        var filteredArchive = CustomFilterQueryTokenProvider.ApplyRule("*.tar.gz; *.zip", results);
        Assert.HasCount(1, filteredArchive);
        Assert.AreEqual("archive.tar.gz", filteredArchive[0].Name);

        var filteredFolder = CustomFilterQueryTokenProvider.ApplyRule(":f", results);
        Assert.HasCount(1, filteredFolder);
        Assert.AreEqual("subfolder", filteredFolder[0].Name);
    }

    [TestMethod]
    public async Task ApplyAsync_MultipleKeywordsWithPipe_CombinesFiltersWithOrLogic()
    {
        var provider = new CustomFilterQueryTokenProvider();
        var results = new List<ISearchResult>
        {
            new FakeSearchResult { Name = "report.docx", FullPath = @"C:\docs\report.docx", IsDir = false },
            new FakeSearchResult { Name = "photo.jpg", FullPath = @"C:\pics\photo.jpg", IsDir = false },
            new FakeSearchResult { Name = "movie.mp4", FullPath = @"C:\videos\movie.mp4", IsDir = false },
        };

        var filtered = await provider.ApplyAsync("@doc|img", results);
        Assert.HasCount(2, filtered);
        Assert.IsTrue(filtered.Any(r => r.Name == "report.docx"));
        Assert.IsTrue(filtered.Any(r => r.Name == "photo.jpg"));
    }

    [TestMethod]
    public void ExpandRule_ResolvesReferencesAndRemovesDuplicatePatterns()
    {
        var filters = new List<CustomFilterItem>
        {
            new() { Keyword = "scripts", Rule = "*.exe; *.cmd" },
            new() { Keyword = "tools", Rule = "*.cmd; *.bat" }
        };

        var expanded = CustomFilterQueryTokenProvider.ExpandRule("@scripts; @tools; *.exe", filters);

        Assert.AreEqual("*.exe; *.cmd; *.bat", expanded);
    }

    [TestMethod]
    public void ExpandRule_UnknownOrCyclicReferenceContributesNoRule()
    {
        var filters = new List<CustomFilterItem>
        {
            new() { Keyword = "a", Rule = "@b" },
            new() { Keyword = "b", Rule = "@a" }
        };

        Assert.AreEqual(string.Empty, CustomFilterQueryTokenProvider.ExpandRule("@missing; @a", filters));
    }

    [TestMethod]
    public void DefaultFilters_ContainsStandardTypeCategories()
    {
        var defaults = CustomFilterQueryTokenProvider.DefaultFilters();
        Assert.IsNotNull(defaults);
        Assert.IsTrue(defaults.Any(f => f.Keyword == "doc"));
        Assert.IsTrue(defaults.Any(f => f.Keyword == "img"));
        Assert.IsTrue(defaults.Any(f => f.Keyword == "video"));
        Assert.IsTrue(defaults.Any(f => f.Keyword == "audio"));
        Assert.IsTrue(defaults.Any(f => f.Keyword == "zip"));
    }

    [TestMethod]
    public void DefaultFilters_IncludeNewDocumentImageAndArchiveExtensions()
    {
        var defaults = CustomFilterQueryTokenProvider.DefaultFilters();

        var docRule = defaults.First(f => f.Keyword == "doc").Rule;
        foreach (var ext in new[] { "*.et", "*.dps", "*.odf", "*.odt", "*.ods", "*.odg", "*.odb", "*.eqp", "*.mmx", "*.tex" })
            Assert.Contains(ext, docRule, $"doc rule missing {ext}");

        var imgRule = defaults.First(f => f.Keyword == "img").Rule;
        foreach (var ext in new[] { "*.jxl", "*.avif" })
            Assert.Contains(ext, imgRule, $"img rule missing {ext}");

        var zipRule = defaults.First(f => f.Keyword == "zip").Rule;
        foreach (var ext in new[] { "*.wim", "*.esd" })
            Assert.Contains(ext, zipRule, $"zip rule missing {ext}");
    }
}
