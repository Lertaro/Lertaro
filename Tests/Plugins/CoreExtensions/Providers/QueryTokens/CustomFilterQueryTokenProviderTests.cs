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
        SearchSyntaxService.TokenPrefixFunc = null;
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
    public void CanHandle_BackslashPrefixedToken_ReturnsTrue()
    {
        var provider = new CustomFilterQueryTokenProvider();
        Assert.IsTrue(provider.CanHandle("\\doc"));
        Assert.IsTrue(provider.CanHandle("\\video"));
        Assert.IsFalse(provider.CanHandle("\\"));
        Assert.IsFalse(provider.CanHandle("doc"));
        Assert.IsFalse(provider.CanHandle("@doc"));
    }

    // The prefix is the HOST's, and a plugin reading it here is what makes the two impossible to disagree:
    // the scanner lifts a word out of the query by its own character and hands the token over with that
    // character still on the front, so a plugin with a stored copy of its own could only ever match or
    // silently claim nothing. This case used to configure that copy through PluginSettingsService.
    [TestMethod]
    public void CanHandle_HostConfiguredPrefix_IsTheOneThatIsMatched()
    {
        SearchSyntaxService.TokenPrefixFunc = () => '!';
        var provider = new CustomFilterQueryTokenProvider();

        Assert.IsTrue(provider.CanHandle("!doc"));
        Assert.IsFalse(provider.CanHandle("\\doc"));

        SearchSyntaxService.TokenPrefixFunc = () => '\\';
        Assert.IsTrue(provider.CanHandle("\\doc"));
        Assert.IsFalse(provider.CanHandle("!doc"));
    }

    [TestMethod]
    public async Task ApplyAsync_CategoryKeyword_MatchesByExtensionRegex()
    {
        var provider = new CustomFilterQueryTokenProvider();
        var results = new List<ISearchResult>
        {
            new FakeSearchResult { Name = "report.docx", FullPath = @"C:\docs\report.docx", IsDir = false },
            new FakeSearchResult { Name = "photo.jpg", FullPath = @"C:\pics\photo.jpg", IsDir = false },
            new FakeSearchResult { Name = "song.mp3", FullPath = @"C:\music\song.mp3", IsDir = false },
            new FakeSearchResult { Name = "subfolder", FullPath = @"C:\docs\subfolder", IsDir = true },
        };

        var filtered = await provider.ApplyAsync("\\doc", results);

        Assert.HasCount(1, filtered);
        Assert.AreEqual("report.docx", filtered[0].Name);
    }

    [TestMethod]
    public async Task ApplyAsync_AudioCategory_MatchesTheAudioExtensions()
    {
        var provider = new CustomFilterQueryTokenProvider();
        var results = new List<ISearchResult>
        {
            new FakeSearchResult { Name = "song.mp3", FullPath = @"C:\music\song.mp3", IsDir = false },
            new FakeSearchResult { Name = "clip.ogg", FullPath = @"C:\music\clip.ogg", IsDir = false },
            new FakeSearchResult { Name = "photo.jpg", FullPath = @"C:\pics\photo.jpg", IsDir = false },
        };

        var filtered = await provider.ApplyAsync("\\audio", results);

        Assert.HasCount(2, filtered);
    }

    [TestMethod]
    public async Task ApplyAsync_LongestKeywordWins_SoAudioBeatsA()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) => key == CustomFilterQueryTokenProvider.SettingKey
            ? new List<CustomFilterItem>
            {
                new() { Keyword = "a", Rule = "*.jpg" },
                new() { Keyword = "audio", Rule = "*.mp3" }
            }
            : fallback;

        var provider = new CustomFilterQueryTokenProvider();
        var results = new List<ISearchResult>
        {
            new FakeSearchResult { Name = "song.mp3", FullPath = @"C:\music\song.mp3", IsDir = false },
            new FakeSearchResult { Name = "photo.jpg", FullPath = @"C:\pics\photo.jpg", IsDir = false },
        };

        var filtered = await provider.ApplyAsync("\\audio", results);

        Assert.HasCount(1, filtered);
        Assert.AreEqual("song.mp3", filtered[0].Name);
    }

    [TestMethod]
    public async Task ApplyAsync_UnknownKeyword_ReturnsNothing()
    {
        var provider = new CustomFilterQueryTokenProvider();
        var results = new List<ISearchResult>
        {
            new FakeSearchResult { Name = "report.docx", FullPath = @"C:\docs\report.docx", IsDir = false },
        };

        var filtered = await provider.ApplyAsync("\\nosuchcategory", results);

        Assert.IsEmpty(filtered);
    }

    [TestMethod]
    public void RuleToRegex_WildcardExtensions_BecomeAnAnchoredAlternation()
    {
        var regex = new System.Text.RegularExpressions.Regex(
            CustomFilterQueryTokenProvider.RuleToRegex("*.mp3; *.wav; *.ogg"),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Assert.IsTrue(regex.IsMatch("song.mp3"));
        Assert.IsTrue(regex.IsMatch("clip.OGG"));
        Assert.IsFalse(regex.IsMatch("clip.mp3.bak"));
        Assert.IsFalse(regex.IsMatch("song.mp4"));
    }

    [TestMethod]
    public void RuleToRegex_BareWord_IsReadAsAnExtension()
    {
        var regex = new System.Text.RegularExpressions.Regex(
            CustomFilterQueryTokenProvider.RuleToRegex("audio"),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Assert.IsTrue(regex.IsMatch("song.audio"));
        Assert.IsFalse(regex.IsMatch("audiofile.txt"));
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
    public void ExpandRule_ResolvesReferencesAndRemovesDuplicatePatterns()
    {
        var filters = new List<CustomFilterItem>
        {
            new() { Keyword = "scripts", Rule = "*.exe; *.cmd" },
            new() { Keyword = "tools", Rule = "*.cmd; *.bat" }
        };

        var expanded = CustomFilterQueryTokenProvider.ExpandRule("\\scripts; \\tools; *.exe", filters);

        Assert.AreEqual("*.exe; *.cmd; *.bat", expanded);
    }

    [TestMethod]
    public void ExpandRule_UnknownOrCyclicReferenceContributesNoRule()
    {
        var filters = new List<CustomFilterItem>
        {
            new() { Keyword = "a", Rule = "\\b" },
            new() { Keyword = "b", Rule = "\\a" }
        };

        Assert.AreEqual(string.Empty, CustomFilterQueryTokenProvider.ExpandRule("\\missing; \\a", filters));
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

    // A compiled rule regex is cached per translated pattern. The key space is one entry per distinct
    // rule TEXT, and every intermediate state of editing a rule is a distinct text, so the cache is fed
    // by unbounded input even though a user configures only a handful of filters at a time.
    [TestMethod]
    public async Task ApplyAsync_ManyDistinctRules_KeepsTheRegexCacheBounded()
    {
        var revision = 0;
        PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) => key == CustomFilterQueryTokenProvider.SettingKey
            ? new List<CustomFilterItem> { new() { Keyword = "doc", Rule = $"*.rev{revision}" } }
            : fallback;

        var provider = new CustomFilterQueryTokenProvider();
        var results = new List<ISearchResult>
        {
            new FakeSearchResult { Name = "report.rev0", FullPath = @"C:\docs\report.rev0" },
        };

        for (var i = 0; i < 600; i++)
        {
            revision = i;
            _ = await provider.ApplyAsync("\\doc", results);
        }

        Assert.IsLessThanOrEqualTo(256, CustomFilterQueryTokenProvider.CachedRegexCount);
    }
}
