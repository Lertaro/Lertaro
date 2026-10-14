using System.IO;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Services;
using Shortcut = Lertaro.App.ViewModels.Search.Mapping.WindowsRecentCandidateMapper.Shortcut;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class WindowsRecentCandidateMapperTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void Collect_MatchesShortcutNames_AndResolvesOnlyTheHits()
    {
        var resolved = new List<string>();
        var shortcuts = new[]
        {
            Lnk("code-atlas", Now),
            Lnk("budget.xlsx", Now),
        };

        var results = Collect("atlas", shortcuts, s => { resolved.Add(s.TargetName); return Target(s); });

        Assert.HasCount(1, results);
        Assert.AreEqual(@"D:\Projects\code-atlas", results[0].Result.FullPath);
        Assert.IsTrue(results[0].Result.IsDir);
        CollectionAssert.AreEqual(new[] { "code-atlas" }, resolved);
    }

    [TestMethod]
    public void Collect_BetterShapeFirst_ThenMostRecentlyOpened()
    {
        var shortcuts = new[]
        {
            Lnk("AutofillStates", Now),
            Lnk("atlas-old.txt", Now.AddDays(-10)),
            Lnk("atlas-new.txt", Now.AddHours(-1)),
        };

        var results = Collect("atlas", shortcuts);

        CollectionAssert.AreEqual(
            new[] { @"D:\Projects\atlas-new.txt", @"D:\Projects\atlas-old.txt", @"D:\Projects\AutofillStates" },
            results.Select(c => c.Result.FullPath).ToList());
    }

    // The shell leaves shortcuts behind for weeks after their target is deleted or its drive unplugged.
    [TestMethod]
    public void Collect_UnresolvableOrMissingTargets_AreSkipped()
    {
        var shortcuts = new[] { Lnk("atlas-gone.txt", Now), Lnk("atlas-unresolvable.txt", Now) };

        var results = WindowsRecentCandidateMapper.Collect(
            FuzzyQuery.Parse("atlas"), null, shortcuts,
            s => s.TargetName == "atlas-gone.txt" ? @"D:\Projects\atlas-gone.txt" : null,
            _ => false, _ => false);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void Collect_PathOutsideScope_IsSkipped()
    {
        var results = WindowsRecentCandidateMapper.Collect(
            FuzzyQuery.Parse("atlas"), @"C:\Elsewhere", [Lnk("atlas.txt", Now)], Target, _ => true, _ => false);

        Assert.IsEmpty(results);
    }

    // Two shortcuts can resolve to the same file: opened from two apps, or the shell rewriting an entry.
    [TestMethod]
    public void Collect_TwoShortcutsToOneTarget_YieldOneRow()
    {
        var shortcuts = new[] { Lnk("atlas.txt", Now), Lnk("atlas.txt", Now.AddDays(-1)) };

        Assert.HasCount(1, Collect("atlas", shortcuts));
    }

    [TestMethod]
    public void Collect_IsCapped()
    {
        var shortcuts = Enumerable.Range(0, 40).Select(i => Lnk($"atlas{i}.txt", Now.AddMinutes(-i))).ToArray();

        Assert.HasCount(20, Collect("atlas", shortcuts));
    }

    // User decision: Lertaro picks first, then Windows Recent Items, then everything else -- even when the
    // ordinary row is the better match.
    [TestMethod]
    public void RankAndDedupe_RecentItemsRankBelowLertaroPicksAndAboveOrdinaryResults()
    {
        var recent = Collect("atlas", [Lnk("atlas-notes.txt", Now)]);
        var history = HistorySearchCandidateMapper.Collect(
            FuzzyQuery.Parse("atlas"), null,
            [new HistoryEntry("atlas", @"D:\Projects\code-atlas", HistoryEntryKind.Folder, 100)],
            _ => false, _ => true);
        var ordinary = new SearchResultMapper.RankedCandidate(
            new AppSearchResult { FullPath = @"C:\atlas", Name = "atlas" },
            IsCurated: false, Priority: int.MaxValue, TypeRank: int.MaxValue,
            new MatchRank(MatchRank.TierName, 0, 1.0), @"C:\atlas");

        var ranked = SearchResultMapper.RankAndDedupe([ordinary, .. recent, .. history]);

        CollectionAssert.AreEqual(
            new[] { @"D:\Projects\code-atlas", @"D:\Projects\atlas-notes.txt", @"C:\atlas" },
            ranked.Select(r => r.FullPath).ToList());
    }

    private static List<SearchResultMapper.RankedCandidate> Collect(string query, Shortcut[] shortcuts, Func<Shortcut, string?>? resolve = null) =>
        WindowsRecentCandidateMapper.Collect(
            FuzzyQuery.Parse(query), null, shortcuts, resolve ?? Target,
            path => Path.HasExtension(path), path => !Path.HasExtension(path));

    private static Shortcut Lnk(string targetName, DateTime lastWriteUtc) =>
        new($@"C:\Recent\{targetName}.lnk", targetName, lastWriteUtc);

    private static string? Target(Shortcut shortcut) => $@"D:\Projects\{shortcut.TargetName}";
}
