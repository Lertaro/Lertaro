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
    public void Reset()
    {
        PluginSettingsService.GetSettingFunc = null;
        // The merge test writes settings back, and a stale writer here would leak its captures into the next test.
        PluginSettingsService.SetSettingFunc = null;
    }

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

    [TestMethod]
    public void MergeSidebarRows_ARowNamingAnExistingEntry_SwitchesThatEntryOn()
    {
        // The usual old configuration was a sidebar row that only mirrored a keyword. Merging must produce ONE
        // entry -- its own rule kept, the row flag set -- rather than two entries answering to one keyword.
        var saved = new Dictionary<string, object?>();
        PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) => key switch
        {
            CustomFilterQueryTokenProvider.SettingKey => new List<CustomFilterItem>
            {
                new() { Keyword = "doc", Rule = "*.md" },
            },
            CustomFilterQueryTokenProvider.LegacySidebarSettingKey => new List<CustomFilterItem>
            {
                new() { Keyword = "doc", Rule = "*.doc; *.md", Icon = "M4 4" },
            },
            _ => fallback,
        };
        PluginSettingsService.SetSettingFunc = (pluginId, key, value) => saved[key] = value;

        CustomFilterQueryTokenProvider.MergeSidebarRowsIntoFiltersOnce();

        var merged = (List<CustomFilterItem>)saved[CustomFilterQueryTokenProvider.SettingKey]!;
        Assert.AreEqual(1, merged.Count, "one keyword, one entry");
        Assert.IsTrue(merged[0].ShowAsRow);
        Assert.AreEqual("*.md", merged[0].Rule, "the entry's own rule wins over the row's copy of it");
        Assert.AreEqual("M4 4", merged[0].Icon, "an icon the row carried is not thrown away");
        Assert.IsNull(saved[CustomFilterQueryTokenProvider.LegacySidebarSettingKey], "the old list is removed, not left to drift");
    }

    [TestMethod]
    public void MergeSidebarRows_ARowWithItsOwnRule_KeepsItUnderItsOwnKeyword()
    {
        var saved = new Dictionary<string, object?>();
        PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) => key switch
        {
            CustomFilterQueryTokenProvider.SettingKey => new List<CustomFilterItem>
            {
                new() { Keyword = "doc", Rule = "*.md" },
            },
            CustomFilterQueryTokenProvider.LegacySidebarSettingKey => new List<CustomFilterItem>
            {
                new() { Keyword = "scripts", Rule = "*.ps1; *.cmd" },
            },
            _ => fallback,
        };
        PluginSettingsService.SetSettingFunc = (pluginId, key, value) => saved[key] = value;

        CustomFilterQueryTokenProvider.MergeSidebarRowsIntoFiltersOnce();

        var merged = (List<CustomFilterItem>)saved[CustomFilterQueryTokenProvider.SettingKey]!;
        var row = merged.Single(f => f.Keyword == "scripts");

        Assert.AreEqual("*.ps1; *.cmd", row.Rule);
        Assert.AreEqual("scripts", row.Label, "the name it was already showing stays its display name");
        Assert.IsTrue(row.ShowAsRow);
    }

    [TestMethod]
    public void MergeSidebarRows_NoLegacyList_StoredNothingAndDefaultsAreUntouched()
    {
        // Idempotence matters more here than speed: the check runs on every read of the list, so a user who has
        // already been merged must not see a second write, and the shipped defaults must not gain row flags --
        // the built-in type rows are already on, and a flagged default would show 文档 twice.
        var saved = new Dictionary<string, object?>();
        PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) => fallback;
        PluginSettingsService.SetSettingFunc = (pluginId, key, value) => saved[key] = value;

        CustomFilterQueryTokenProvider.MergeSidebarRowsIntoFiltersOnce();

        Assert.IsEmpty(saved);
        Assert.IsTrue(CustomFilterQueryTokenProvider.DefaultFilters().All(f => !f.ShowAsRow));
    }

    [TestMethod]
    public async Task ApplyAsync_ARowFlaggedEntry_StillAnswersToItsKeyword()
    {
        // Flagging an entry as a row must not cost it the typed trigger: one entry, two ways to reach it.
        PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) => key switch
        {
            CustomFilterQueryTokenProvider.SettingKey => new List<CustomFilterItem>
            {
                new() { Keyword = "doc", Rule = "*.md", ShowAsRow = true },
                new() { Keyword = "other", Rule = "*.txt" },
            },
            _ => fallback,
        };

        var kept = await new CustomFilterQueryTokenProvider().ApplyAsync("@doc",
            new List<FakeSearchResult> { new() { Name = "a.md" }, new() { Name = "b.txt" } });

        Assert.AreEqual(1, kept.Count);
        Assert.AreEqual("a.md", kept[0].Name);
    }

    [TestMethod]
    public void DefaultFilters_AlsoOfferTheAppKeywordThatTheFilterTabsAlreadyAnswer()
    {
        // A user asking for "only programs" had to write their own rule; the quick window's 应用 tab already
        // knew the answer, and now :@app gives the same one.
        var app = CustomFilterQueryTokenProvider.DefaultFilters().Single(f => f.Keyword == "app");

        Assert.IsTrue(app.Enabled);
        Assert.AreEqual(PluginSdk.Helpers.SearchCategoryClassifier
            .RuleOf(PluginSdk.Helpers.SearchCategoryFilter.Application), app.Rule);
    }

    [TestMethod]
    public void DefaultFiltersSchema_IsTheSameListTheRuntimeFallsBackTo()
    {
        // These used to be two hand-written copies of the same five rules, so a rule edited in one of them
        // silently changed what the settings page calls its default without changing what :@doc matches.
        var runtime = CustomFilterQueryTokenProvider.DefaultFilters();
        var schema = CustomFilterQueryTokenProvider.DefaultFiltersSchema()
            .Cast<Dictionary<string, object>>()
            .ToList();

        Assert.AreEqual(runtime.Count, schema.Count);
        for (var i = 0; i < runtime.Count; i++)
        {
            Assert.AreEqual(runtime[i].Keyword, schema[i]["Keyword"]);
            Assert.AreEqual(runtime[i].Rule, schema[i]["Rule"]);
            Assert.AreEqual(runtime[i].Enabled, schema[i]["Enabled"]);
        }
    }
}
