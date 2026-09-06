using Lertaro.Plugins.BrowserData.Readers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.BrowserData.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BrowserDataInstantProviderTests
{
    [TestInitialize]
    public void ResetBefore()
    {
        FuzzyMatchService.IsMatchFunc = null;
        FuzzyMatchService.GetHighlightMaskFunc = null;
        PluginSettingsService.GetSettingFunc = null;
    }

    [TestCleanup]
    public void ResetAfter()
    {
        FuzzyMatchService.IsMatchFunc = null;
        FuzzyMatchService.GetHighlightMaskFunc = null;
        PluginSettingsService.GetSettingFunc = null;
    }

    private static ProfileEntries CreateProfile(string name, params BrowserEntry[] entries)
    {
        var profile = new ProfileEntries
        {
            Profile = new BrowserProfileConfig { Name = name, Path = "C:\\dummy" },
            Family = BrowserFamily.Chromium
        };
        profile.Bookmarks.AddRange(entries.Where(e => e.IsBookmark));
        profile.History.AddRange(entries.Where(e => !e.IsBookmark));
        return profile;
    }

    [TestMethod]
    public void FormatDescription_BookmarkWithProfileName_ReturnsProfileAndUrl()
    {
        var entry = new BrowserEntry("Example", "https://example.com", isBookmark: true, sortKey: 0);
        var profile = CreateProfile("Chrome", entry);

        var description = BrowserDataInstantProvider.FormatDescription(entry, profile);

        Assert.AreEqual("Chrome · https://example.com", description);
    }

    [TestMethod]
    public void FormatDescription_BookmarkWithoutProfileName_ReturnsUrlOnly()
    {
        var entry = new BrowserEntry("Example", "https://example.com", isBookmark: true, sortKey: 0);
        var profile = CreateProfile(string.Empty, entry);

        var description = BrowserDataInstantProvider.FormatDescription(entry, profile);

        Assert.AreEqual("https://example.com", description);
    }

    [TestMethod]
    public void FormatDescription_HistoryWithTimeAndProfileName_ReturnsTimeProfileAndUrl()
    {
        // Chromium timestamp for 2026-01-01 00:00:00 UTC (microseconds since 1601-01-01)
        var chromiumTicks = (new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) - new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero)).Ticks / 10;
        var entry = new BrowserEntry("Example", "https://example.com", isBookmark: false, sortKey: chromiumTicks);
        var profile = CreateProfile("Chrome", entry);

        var description = BrowserDataInstantProvider.FormatDescription(entry, profile);

        Assert.Contains("Chrome · https://example.com", description);
        Assert.Contains(BrowserHistoryTime.Format(entry.VisitTime!.Value), description);
    }

    [TestMethod]
    public void CollectMatches_SingleCharacter_MatchesTitleAtTierZero()
    {
        var entry = new BrowserEntry("GitHub", "https://example.com", isBookmark: true, sortKey: 100);
        var profile = CreateProfile("Chrome", entry);
        var matches = new List<(BrowserEntry Entry, ProfileEntries Profile, int Tier)>();

        BrowserDataInstantProvider.CollectMatches(new List<BrowserEntry> { entry }, profile, "G", matches);

        Assert.HasCount(1, matches);
        Assert.AreEqual(0, matches[0].Tier);
    }

    [TestMethod]
    public void CollectMatches_SingleCharacter_MatchesUrlAtTierOne()
    {
        var entry = new BrowserEntry("Sample", "https://google.com", isBookmark: true, sortKey: 100);
        var profile = CreateProfile("Chrome", entry);
        var matches = new List<(BrowserEntry Entry, ProfileEntries Profile, int Tier)>();

        BrowserDataInstantProvider.CollectMatches(new List<BrowserEntry> { entry }, profile, "g", matches);

        Assert.HasCount(1, matches);
        Assert.AreEqual(1, matches[0].Tier);
    }

    [TestMethod]
    public void CollectMatches_ProfileNameMatches_ReturnsTierTwo()
    {
        var entry = new BrowserEntry("Foo", "https://bar.com", isBookmark: true, sortKey: 100);
        var profile = CreateProfile("Chrome", entry);
        var matches = new List<(BrowserEntry Entry, ProfileEntries Profile, int Tier)>();

        BrowserDataInstantProvider.CollectMatches(new List<BrowserEntry> { entry }, profile, "Chrome", matches);

        Assert.HasCount(1, matches);
        Assert.AreEqual(2, matches[0].Tier);
    }

    [TestMethod]
    public void CollectMatches_HistoryTimeStringMatches_ReturnsTierTwo()
    {
        var chromiumTicks = (new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) - new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero)).Ticks / 10;
        var entry = new BrowserEntry("Portal", "https://corp.internal", isBookmark: false, sortKey: chromiumTicks);
        var profile = CreateProfile("Default", entry);
        var matches = new List<(BrowserEntry Entry, ProfileEntries Profile, int Tier)>();

        var formattedTime = BrowserHistoryTime.Format(entry.VisitTime!.Value);
        var searchPart = formattedTime.Substring(0, 7); // e.g. "2026/01"

        BrowserDataInstantProvider.CollectMatches(new List<BrowserEntry> { entry }, profile, searchPart, matches);

        Assert.HasCount(1, matches);
        Assert.AreEqual(2, matches[0].Tier);
    }

    [TestMethod]
    public void CollectMatches_FuzzyMatchFallback_ReturnsTierThree()
    {
        var entry = new BrowserEntry("Visual Studio Code", "https://example.com", isBookmark: true, sortKey: 100);
        var profile = CreateProfile("Browser", entry);
        var matches = new List<(BrowserEntry Entry, ProfileEntries Profile, int Tier)>();

        FuzzyMatchService.IsMatchFunc = (pattern, text) => pattern == "vsc" && text == "Visual Studio Code";

        BrowserDataInstantProvider.CollectMatches(new List<BrowserEntry> { entry }, profile, "vsc", matches);

        Assert.HasCount(1, matches);
        Assert.AreEqual(3, matches[0].Tier);
    }

    [TestMethod]
    public void CollectMatches_EmptyQuery_MatchesAllWithTierZero()
    {
        var e1 = new BrowserEntry("A", "https://a.com", isBookmark: true, sortKey: 10);
        var e2 = new BrowserEntry("B", "https://b.com", isBookmark: true, sortKey: 20);
        var profile = CreateProfile("Chrome", e1, e2);
        var matches = new List<(BrowserEntry Entry, ProfileEntries Profile, int Tier)>();

        BrowserDataInstantProvider.CollectMatches(new List<BrowserEntry> { e1, e2 }, profile, string.Empty, matches);

        Assert.HasCount(2, matches);
        Assert.AreEqual(0, matches[0].Tier);
        Assert.AreEqual(0, matches[1].Tier);
    }

    [TestMethod]
    public void GetHighlightMask_SingleCharacterTerm_ReturnsMaskFromService()
    {
        FuzzyMatchService.GetHighlightMaskFunc = (text, term) => new bool[text.Length];
        var provider = new BrowserDataInstantProvider();

        var mask = provider.GetHighlightMask("Example Title", "bb E");

        Assert.IsNotNull(mask);
        Assert.HasCount("Example Title".Length, mask);
    }

    [TestMethod]
    public void GetHighlightMask_EmptySearchTerm_ReturnsNull()
    {
        var provider = new BrowserDataInstantProvider();

        var mask = provider.GetHighlightMask("Example Title", "bb ");

        Assert.IsNull(mask);
    }
}
