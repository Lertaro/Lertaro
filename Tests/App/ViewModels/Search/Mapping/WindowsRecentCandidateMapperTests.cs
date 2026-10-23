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

    // Windows Recent rows are never curated and a single open is not a habit: both stay below an ordinary
    // row whose name the query actually explains, and rank among themselves by name.
    [TestMethod]
    public void RankAndDedupe_RecentAndOneOpenHistoryRankBelowAnExactMatch()
    {
        var recent = Collect("atlas", [Lnk("atlas-notes.txt", Now)]);
        var history = HistorySearchCandidateMapper.Collect(
            FuzzyQuery.Parse("atlas"), null,
            [new HistoryEntry("atlas", @"D:\Projects\code-atlas", HistoryEntryKind.Folder, Time: 100, Count: 1)],
            _ => false, _ => true);
        var ordinary = new SearchResultMapper.RankedCandidate(
            new AppSearchResult { FullPath = @"C:\atlas", Name = "atlas" },
            IsFavorite: false, HistoryCount: 0, TypeRank: int.MaxValue,
            new MatchRank(MatchRank.TierName, 0, 1.0, 1.0), @"C:\atlas");

        var ranked = SearchResultMapper.RankAndDedupe([ordinary, .. recent, .. history]);

        // The history row's one open is 100 (under the 300 threshold), so it is not lifted at all: it keeps
        // the history-tail section, which sits after the low-priority one. The Recent row scores on its
        // name match alone.
        CollectionAssert.AreEqual(
            new[] { @"C:\atlas", @"D:\Projects\atlas-notes.txt", @"D:\Projects\code-atlas" },
            ranked.Select(r => r.FullPath).ToList());
    }

    [TestMethod]
    public void RankAndDedupe_HistoryRowWithHundredOpens_StaysBehindTheDirectNameRow()
    {
        var history = HistorySearchCandidateMapper.Collect(
            FuzzyQuery.Parse("atlas"), null,
            [new HistoryEntry("atlas", @"D:\Projects\code-atlas", HistoryEntryKind.Folder, Time: 100, Count: 100)],
            _ => false, _ => true);
        var ordinary = new SearchResultMapper.RankedCandidate(
            new AppSearchResult { FullPath = @"C:\atlas", Name = "atlas" },
            IsFavorite: false, HistoryCount: 0, TypeRank: int.MaxValue,
            new MatchRank(MatchRank.TierName, 0, 1.0, 1.0), @"C:\atlas");

        var ranked = SearchResultMapper.RankAndDedupe([ordinary, .. history]);

        // A hundred opens decay to well over the 5.0 threshold, so the row reaches H5 -- but its own name
        // never matched, which puts it in the learned tail. Under V3 no amount of opening crosses the
        // direct name row's section; that 34-beats-18 crossing was the exchange rate this replaces.
        CollectionAssert.AreEqual(
            new[] { @"C:\atlas", @"D:\Projects\code-atlas" },
            ranked.Select(r => r.FullPath).ToList());
    }

    // The bar the index side holds its own rows to, applied to a row only this window can build: a shortcut to
    // something hidden is not a row the full window could ever list either, so it must not be listed here
    // alone (see HistorySearchCandidateMapper.IsExcludedFromSearch).
    [TestMethod]
    public void Collect_HiddenLocalFile_IsSkipped()
    {
        var directory = Directory.CreateTempSubdirectory("lertaro-hidden-recent-").FullName;
        var path = Path.Combine(directory, "atlas.png");
        File.WriteAllText(path, "x");
        try
        {
            var shortcut = Lnk("atlas.png", Now);

            var visible = WindowsRecentCandidateMapper.Collect(FuzzyQuery.Parse("atlas"), null, [shortcut],
                _ => path, File.Exists, Directory.Exists);
            Assert.HasCount(1, visible, "the control: the same row while the file is not hidden");

            File.SetAttributes(path, FileAttributes.Hidden);

            Assert.IsEmpty(WindowsRecentCandidateMapper.Collect(FuzzyQuery.Parse("atlas"), null, [shortcut],
                _ => path, File.Exists, Directory.Exists));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static List<SearchResultMapper.RankedCandidate> Collect(string query, Shortcut[] shortcuts, Func<Shortcut, string?>? resolve = null) =>
        WindowsRecentCandidateMapper.Collect(
            FuzzyQuery.Parse(query), null, shortcuts, resolve ?? Target,
            path => Path.HasExtension(path), path => !Path.HasExtension(path));

    private static Shortcut Lnk(string targetName, DateTime lastWriteUtc) =>
        new($@"C:\Recent\{targetName}.lnk", targetName, lastWriteUtc);

    private static string? Target(Shortcut shortcut) => $@"D:\Projects\{shortcut.TargetName}";
}
