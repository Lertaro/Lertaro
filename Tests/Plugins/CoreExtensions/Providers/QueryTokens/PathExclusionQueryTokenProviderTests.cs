using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.QueryTokens;

[TestClass]
[DoNotParallelize]
public sealed class PathExclusionQueryTokenProviderCanHandleTests
{
    [TestInitialize]
    [TestCleanup]
    public void Reset() => PluginSettingsService.GetSettingFunc = null;

    [TestMethod]
    public void CanHandle_ColonPrefixedToken_ReturnsTrue() => Assert.IsTrue(new PathExclusionQueryTokenProvider().CanHandle(":rena"));

    [TestMethod]
    public void CanHandle_JustColon_ReturnsFalse() => Assert.IsFalse(new PathExclusionQueryTokenProvider().CanHandle(":"));

    [TestMethod]
    public void CanHandle_NoColonPrefix_ReturnsFalse() => Assert.IsFalse(new PathExclusionQueryTokenProvider().CanHandle("rena"));

    [TestMethod]
    public void GetHighlightText_StripsLeadingColon() => Assert.AreEqual("rena", new PathExclusionQueryTokenProvider().GetHighlightText(":rena"));

    [TestMethod]
    public void GetHighlightText_JustColon_ReturnsNull() => Assert.IsNull(new PathExclusionQueryTokenProvider().GetHighlightText(":"));

    [TestMethod]
    public void CanHandle_CustomPrefix_ReturnsTrue()
    {
        PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) => key == PathExclusionQueryTokenProvider.SettingKey ? "#" : fallback;
        Assert.IsTrue(new PathExclusionQueryTokenProvider().CanHandle("#rena"));
        Assert.IsFalse(new PathExclusionQueryTokenProvider().CanHandle(":rena"));
        Assert.AreEqual("rena", new PathExclusionQueryTokenProvider().GetHighlightText("#rena"));
    }
}

// FuzzyMatchService.IsMatchFunc is a shared static delegate (null by default -- IsMatch always returns
// false unwired) -- these tests wire in a simple deterministic substring matcher so the real
// segment-splitting/filtering logic in ApplyAsync can be exercised. [DoNotParallelize] plus resetting in
// TestCleanup keeps tests in this class from racing on it.
[TestClass]
[DoNotParallelize]
public sealed class PathExclusionQueryTokenProviderApplyTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    [TestInitialize]
    public void WireFuzzyMatch() =>
        FuzzyMatchService.IsMatchFunc = (pattern, text) => text.Contains(pattern, StringComparison.OrdinalIgnoreCase);

    [TestCleanup]
    public void Reset() => FuzzyMatchService.IsMatchFunc = null;

    [TestMethod]
    public async Task ApplyAsync_MatchInFileName_IsKept()
    {
        var results = new ISearchResult[] { new FakeResult { FullPath = @"C:\Projects\Rename.cs" } };

        var filtered = await new PathExclusionQueryTokenProvider().ApplyAsync(":rena", results);

        Assert.HasCount(1, filtered);
    }

    [TestMethod]
    public async Task ApplyAsync_MatchInAncestorFolder_IsKept()
    {
        var results = new ISearchResult[] { new FakeResult { FullPath = @"C:\Rename\file.cs" } };

        var filtered = await new PathExclusionQueryTokenProvider().ApplyAsync(":rena", results);

        Assert.HasCount(1, filtered);
    }

    [TestMethod]
    public async Task ApplyAsync_NoSegmentMatches_IsExcluded()
    {
        var results = new ISearchResult[] { new FakeResult { FullPath = @"C:\Projects\Other.cs" } };

        var filtered = await new PathExclusionQueryTokenProvider().ApplyAsync(":rena", results);

        Assert.IsEmpty(filtered);
    }

    [TestMethod]
    public async Task ApplyAsync_EmptyPattern_ReturnsResultsUnchanged()
    {
        var results = new ISearchResult[] { new FakeResult { FullPath = @"C:\a.txt" } };

        var filtered = await new PathExclusionQueryTokenProvider().ApplyAsync(":", results);

        Assert.HasCount(1, filtered);
    }
}
