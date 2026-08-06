namespace Lertaro.Plugins.ProcessManager.Tests;

[TestClass]
public sealed class ProcessManagerInstantProviderTests
{
    [TestMethod]
    public void GetMatchTier_SearchTermInProcessName_ReturnsTierZero()
        => Assert.AreEqual(0, ProcessManagerInstantProvider.GetMatchTier("notepad", "1234", "", "notepad"));

    [TestMethod]
    public void GetMatchTier_SearchTermInPid_ReturnsTierOne()
        => Assert.AreEqual(1, ProcessManagerInstantProvider.GetMatchTier("notepad", "1234", "", "234"));

    [TestMethod]
    public void GetMatchTier_SearchTermInWindowTitle_ReturnsTierTwo()
        => Assert.AreEqual(2, ProcessManagerInstantProvider.GetMatchTier("excel", "5678", "Q4 Report - Excel", "q4 report"));

    [TestMethod]
    public void GetMatchTier_ProcessNameMatchTakesPriorityOverPidOrTitle()
        // "excel" matches the process name literally (tier 0) even though it would also match the
        // window title -- the tier returned must be the strongest one, not just any matching one.
        => Assert.AreEqual(0, ProcessManagerInstantProvider.GetMatchTier("excel", "5678", "excel report", "excel"));

    [TestMethod]
    public void GetMatchTier_IsCaseInsensitive()
        => Assert.AreEqual(0, ProcessManagerInstantProvider.GetMatchTier("EXCEL", "5678", "Q4 Report", "excel"));

    [TestMethod]
    public void GetMatchTier_NoLiteralMatchAndFuzzyUnwired_ReturnsNull()
        => Assert.IsNull(ProcessManagerInstantProvider.GetMatchTier("notepad", "1234", "Untitled", "calculator"));

    [TestMethod]
    public void GetMatchTier_EmptyWindowTitle_DoesNotMatchArbitrarySearchTerm()
        // A background/non-windowed process has an empty MainWindowTitle -- Contains("", ...) would
        // trivially match any search term if this weren't guarded, matching every backgrounded process
        // regardless of what was actually typed.
        => Assert.IsNull(ProcessManagerInstantProvider.GetMatchTier("svchost", "999", "", "notepad"));

    [TestMethod]
    public void GetMatchTier_AnyVisibleWindowTitleCanMatch()
    {
        var tier = ProcessManagerInstantProvider.GetMatchTier(
            "FSViewer",
            "1234",
            ["Settings", "Fast image viewer"],
            "fast");

        Assert.AreEqual(2, tier);
    }
}

// FuzzyMatchService.IsMatchFunc is a shared static delegate (null by default -- IsMatch always returns
// false unwired) -- these tests wire in a deterministic fake so the alias/fuzzy fallback tier (3) can
// actually be exercised, instead of every test above implicitly only covering the literal tiers.
// [DoNotParallelize] plus resetting in TestCleanup keeps tests in this class from racing on the shared
// delegate against other tests in the assembly.
[TestClass]
[DoNotParallelize]
public sealed class ProcessManagerInstantProviderFuzzyMatchTests
{
    [TestInitialize]
    public void WireFuzzyMatch() =>
        PluginSdk.Services.FuzzyMatchService.IsMatchFunc = (pattern, text) => text.Contains(pattern, StringComparison.OrdinalIgnoreCase);

    [TestCleanup]
    public void Reset() => PluginSdk.Services.FuzzyMatchService.IsMatchFunc = null;

    [TestMethod]
    public void GetMatchTier_NameOnlyMatchesViaFuzzyFallback_ReturnsTierThree()
    {
        // Neither the name, PID, nor title literally contain "daili" -- only the wired fuzzy matcher
        // (standing in for the host's real pinyin transliteration, e.g. matching "daili" against a
        // process/title containing "代理") recognizes it.
        PluginSdk.Services.FuzzyMatchService.IsMatchFunc = (pattern, text) => pattern == "daili" && text.Contains("代理");

        var tier = ProcessManagerInstantProvider.GetMatchTier("代理进程", "1234", "", "daili");

        Assert.AreEqual(3, tier);
    }

    [TestMethod]
    public void GetMatchTier_TitleOnlyMatchesViaFuzzyFallback_ReturnsTierThree()
    {
        PluginSdk.Services.FuzzyMatchService.IsMatchFunc = (pattern, text) => pattern == "daili" && text.Contains("代理");

        var tier = ProcessManagerInstantProvider.GetMatchTier("chrome", "1234", "zashboard - 代理", "daili");

        Assert.AreEqual(3, tier);
    }

    [TestMethod]
    public void GetMatchTier_LiteralMatchAlreadyFound_DoesNotNeedFuzzyFallback()
    {
        // If a literal tier already matched, the fuzzy fallback must never even run -- returning false
        // here would still be correct only because it's never reached; this pins tier 0, not 3.
        PluginSdk.Services.FuzzyMatchService.IsMatchFunc = (_, _) => false;

        var tier = ProcessManagerInstantProvider.GetMatchTier("notepad", "1234", "", "notepad");

        Assert.AreEqual(0, tier);
    }

    [TestMethod]
    public void GetMatchTier_NothingMatchesAtAnyTier_ReturnsNull()
    {
        PluginSdk.Services.FuzzyMatchService.IsMatchFunc = (_, _) => false;

        var tier = ProcessManagerInstantProvider.GetMatchTier("notepad", "1234", "Untitled", "calculator");

        Assert.IsNull(tier);
    }
}
