using Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.QueryTokens;

[TestClass]
[DoNotParallelize]
public class WildcardQueryTokenProviderTests
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
    public void CanHandle_TokenStartsWithQuestionMark_ReturnsTrue()
    {
        var provider = new WildcardQueryTokenProvider();
        Assert.IsTrue(provider.CanHandle("?(2026???????????)"));
        Assert.IsTrue(provider.CanHandle("?*.mp4"));
        Assert.IsFalse(provider.CanHandle("?"));
        Assert.IsFalse(provider.CanHandle("mp4"));
    }

    [TestMethod]
    public void CanHandle_CustomPrefix_ReturnsTrue()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) => key == WildcardQueryTokenProvider.PrefixSettingKey ? "~" : fallback;
        var provider = new WildcardQueryTokenProvider();
        Assert.IsTrue(provider.CanHandle("~*.mp4"));
        Assert.IsFalse(provider.CanHandle("?*.mp4"));
    }

    [TestMethod]
    public async Task ApplyAsync_WildcardDateTagInParentheses_FiltersMatchingResults()
    {
        var provider = new WildcardQueryTokenProvider();
        var results = new List<ISearchResult>
        {
            new FakeSearchResult { Name = "asdfwesdfsdfs(202601241243567).mp4", FullPath = @"C:\media\asdfwesdfsdfs(202601241243567).mp4" },
            new FakeSearchResult { Name = "asdfwesdfsdfs(202501241243567).mp4", FullPath = @"C:\media\asdfwesdfsdfs(202501241243567).mp4" },
            new FakeSearchResult { Name = "normal_file.mp4", FullPath = @"C:\media\normal_file.mp4" },
        };

        var filtered = await provider.ApplyAsync("?(2026???????????)", results);
        Assert.HasCount(1, filtered);
        Assert.AreEqual("asdfwesdfsdfs(202601241243567).mp4", filtered[0].Name);
    }

    [TestMethod]
    public async Task ApplyAsync_MultipleWildcardsWithSemicolon_CombinesFiltersWithOrLogic()
    {
        var provider = new WildcardQueryTokenProvider();
        var results = new List<ISearchResult>
        {
            new FakeSearchResult { Name = "doc(20260101).pdf", FullPath = @"C:\docs\doc(20260101).pdf" },
            new FakeSearchResult { Name = "image.png", FullPath = @"C:\pics\image.png" },
            new FakeSearchResult { Name = "video.mp4", FullPath = @"C:\media\video.mp4" },
        };

        var filtered = await provider.ApplyAsync("?(2026*);*.png", results);
        Assert.HasCount(2, filtered);
        Assert.IsTrue(filtered.Any(r => r.Name == "doc(20260101).pdf"));
        Assert.IsTrue(filtered.Any(r => r.Name == "image.png"));
    }
}
