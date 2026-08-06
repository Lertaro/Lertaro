namespace Lertaro.Plugins.WindowSwitcher.Tests;

[TestClass]
public sealed class WindowSwitcherInstantProviderTests
{
    [TestMethod]
    public void GetMatchTier_SearchTermInTitle_ReturnsTierZero()
        => Assert.AreEqual(0, WindowSwitcherInstantProvider.GetMatchTier("Q4 Report - Excel", "excel", "5678", "q4 report"));

    [TestMethod]
    public void GetMatchTier_SearchTermInProcessName_ReturnsTierOne()
        => Assert.AreEqual(1, WindowSwitcherInstantProvider.GetMatchTier("Untitled Document", "notepad", "1234", "notepad"));

    [TestMethod]
    public void GetMatchTier_SearchTermInPid_ReturnsTierTwo()
        => Assert.AreEqual(2, WindowSwitcherInstantProvider.GetMatchTier("Untitled Document", "notepad", "1234", "234"));

    [TestMethod]
    public void GetMatchTier_TitleMatchTakesPriorityOverProcessNameOrPid()
        // "excel" matches the process name literally (tier 1) even though it would also match the title
        // -- the tier returned must be the strongest one (title, tier 0), not just any matching one.
        => Assert.AreEqual(0, WindowSwitcherInstantProvider.GetMatchTier("excel report", "excel", "5678", "excel"));

    [TestMethod]
    public void GetMatchTier_IsCaseInsensitive()
        => Assert.AreEqual(0, WindowSwitcherInstantProvider.GetMatchTier("Q4 REPORT", "excel", "5678", "q4 report"));

    [TestMethod]
    public void GetMatchTier_NoLiteralMatchAndFuzzyUnwired_ReturnsNull()
        => Assert.IsNull(WindowSwitcherInstantProvider.GetMatchTier("Untitled", "notepad", "1234", "calculator"));

    [TestMethod]
    public void GetMatchTier_EmptyTitle_DoesNotMatchArbitrarySearchTerm()
        // A window that slipped through with no real title (shouldn't normally happen given
        // WindowEnumerator's own filtering, but this method takes plain strings, not a live HWND) --
        // Contains("", ...) would trivially match everything if this weren't guarded.
        => Assert.IsNull(WindowSwitcherInstantProvider.GetMatchTier("", "svchost", "999", "notepad"));
}

// FuzzyMatchService.IsMatchFunc is a shared static delegate (null by default -- IsMatch always returns
// false unwired) -- these tests wire in a deterministic fake so the fuzzy/alias fallback tier (3) can
// actually be exercised. [DoNotParallelize] plus resetting in TestCleanup keeps tests in this class from
// racing on the shared delegate against other tests in the assembly.
[TestClass]
[DoNotParallelize]
public sealed class WindowSwitcherInstantProviderFuzzyMatchTests
{
    [TestInitialize]
    public void WireFuzzyMatch() =>
        PluginSdk.Services.FuzzyMatchService.IsMatchFunc = (pattern, text) => text.Contains(pattern, StringComparison.OrdinalIgnoreCase);

    [TestCleanup]
    public void Reset() => PluginSdk.Services.FuzzyMatchService.IsMatchFunc = null;

    [TestMethod]
    public void GetMatchTier_TitleOnlyMatchesViaFuzzyFallback_ReturnsTierThree()
    {
        // Neither the title, process name, nor PID literally contain "daili" -- only the wired fuzzy
        // matcher (standing in for the host's real pinyin transliteration, e.g. matching "daili" against
        // a title containing "代理") recognizes it.
        PluginSdk.Services.FuzzyMatchService.IsMatchFunc = (pattern, text) => pattern == "daili" && text.Contains("代理");

        var tier = WindowSwitcherInstantProvider.GetMatchTier("设置 - 代理", "chrome", "1234", "daili");

        Assert.AreEqual(3, tier);
    }

    [TestMethod]
    public void GetMatchTier_ProcessNameOnlyMatchesViaFuzzyFallback_ReturnsTierThree()
    {
        PluginSdk.Services.FuzzyMatchService.IsMatchFunc = (pattern, text) => pattern == "daili" && text.Contains("代理");

        var tier = WindowSwitcherInstantProvider.GetMatchTier("Untitled", "代理", "1234", "daili");

        Assert.AreEqual(3, tier);
    }

    [TestMethod]
    public void GetMatchTier_LiteralMatchAlreadyFound_DoesNotNeedFuzzyFallback()
    {
        // If a literal tier already matched, the fuzzy fallback must never even run -- returning false
        // here would still be correct only because it's never reached; this pins tier 0, not 3.
        PluginSdk.Services.FuzzyMatchService.IsMatchFunc = (_, _) => false;

        var tier = WindowSwitcherInstantProvider.GetMatchTier("Untitled - Notepad", "notepad", "1234", "notepad");

        Assert.AreEqual(0, tier);
    }

    [TestMethod]
    public void GetMatchTier_NothingMatchesAtAnyTier_ReturnsNull()
    {
        PluginSdk.Services.FuzzyMatchService.IsMatchFunc = (_, _) => false;

        var tier = WindowSwitcherInstantProvider.GetMatchTier("Untitled", "notepad", "1234", "calculator");

        Assert.IsNull(tier);
    }
}
