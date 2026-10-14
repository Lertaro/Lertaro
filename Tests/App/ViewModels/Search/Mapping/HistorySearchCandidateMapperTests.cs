using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class HistorySearchCandidateMapperTests
{
    [TestMethod]
    public void Collect_QueryMatchesRecordedKeyword_AddsExistingPathWithLearnedPriority()
    {
        var entries = new[] { Entry("bcomp", @"C:\Apps\BCompare.exe", HistoryEntryKind.File) };

        var results = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse("bc"), null, entries, _ => true, _ => false);

        Assert.HasCount(1, results);
        Assert.AreEqual(@"C:\Apps\BCompare.exe", results[0].Result.FullPath);
        Assert.IsTrue(results[0].IsCurated);
        Assert.IsLessThan(0, results[0].Priority);
    }

    [TestMethod]
    public void Collect_HistoryMatchesWithEqualQualityPreferHigherCount()
    {
        var entries = new[]
        {
            Entry("bcomp", @"C:\Apps\LessUsed.exe", HistoryEntryKind.File, count: 2),
            Entry("bcomp", @"C:\Apps\MoreUsed.exe", HistoryEntryKind.File, count: 5)
        };

        var results = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse("bc"), null, entries, _ => true, _ => false);

        CollectionAssert.AreEqual(
            new[] { @"C:\Apps\MoreUsed.exe", @"C:\Apps\LessUsed.exe" },
            results.Select(result => result.Result.FullPath).ToList());
    }

    // Picking a lower result for a query makes it the top one next time, even over one opened more often.
    [TestMethod]
    public void Collect_MostRecentPickBeatsMoreFrequentOlderOne()
    {
        var entries = new[]
        {
            Entry("atlas", @"C:\Games\Atlas", HistoryEntryKind.Folder, count: 5, time: 100),
            Entry("atlas", @"D:\Projects\code-atlas", HistoryEntryKind.Folder, count: 1, time: 200)
        };

        var results = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse("atlas"), null, entries, _ => false, _ => true);

        CollectionAssert.AreEqual(
            new[] { @"D:\Projects\code-atlas", @"C:\Games\Atlas" },
            results.Select(result => result.Result.FullPath).ToList());
    }

    // The full window's ladder, as a path -> priority map: this query's picks, then other history, then
    // Windows Recent Items. Paths are not probed, so these test paths need not exist.
    [TestMethod]
    public void RankingPriorities_LearnedPickThenOtherHistoryThenRecentItems()
    {
        var entries = new[]
        {
            Entry("atlas", @"D:\Projects\code-atlas", HistoryEntryKind.Folder),
            Entry("budget", @"C:\Docs\atlas-budget.xlsx", HistoryEntryKind.File),
        };
        var history = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\Docs\atlas-budget.xlsx"] = 0,
            [@"D:\Projects\code-atlas"] = 1,
        };
        var recent = WindowsRecentCandidateMapper.Collect(
            FuzzyQuery.Parse("atlas"), null,
            [new WindowsRecentCandidateMapper.Shortcut(@"C:\Recent\atlas.png.lnk", "atlas.png", DateTime.UtcNow)],
            _ => @"C:\Pictures\atlas.png", _ => true, _ => false);

        var priorities = HistorySearchCandidateMapper.RankingPriorities(FuzzyQuery.Parse("atlas"), entries, history, recent);

        Assert.IsLessThan(priorities[@"C:\Docs\atlas-budget.xlsx"], priorities[@"D:\Projects\code-atlas"]);
        Assert.IsLessThan(priorities[@"C:\Pictures\atlas.png"], priorities[@"C:\Docs\atlas-budget.xlsx"]);
        Assert.IsLessThan(int.MaxValue, priorities[@"C:\Pictures\atlas.png"]);
    }

    [TestMethod]
    public void Collect_HistoryMatchesPreferCountBeforeMatchWeight()
    {
        var entries = new[]
        {
            Entry("bc", @"C:\Apps\Exact.exe", HistoryEntryKind.File, count: 1),
            Entry("bcomp", @"C:\Apps\Frequent.exe", HistoryEntryKind.File, count: 5)
        };

        var results = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse("bc"), null, entries, _ => true, _ => false);

        Assert.AreEqual(@"C:\Apps\Frequent.exe", results[0].Result.FullPath);
    }

    [TestMethod]
    public void Collect_UnrelatedKeyword_DoesNotAddPath()
    {
        var entries = new[] { Entry("other", @"C:\Apps\BCompare.exe", HistoryEntryKind.File) };

        var results = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse("bc"), null, entries, _ => true, _ => false);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void Collect_MissingPath_DoesNotAddPath()
    {
        var entries = new[] { Entry("bcomp", @"C:\Apps\BCompare.exe", HistoryEntryKind.File) };

        var results = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse("bc"), null, entries, _ => false, _ => false);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void Collect_FolderHistory_CreatesDirectoryResult()
    {
        var entries = new[] { Entry("proj", @"C:\Work\Project", HistoryEntryKind.Folder) };

        var results = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse("pro"), null, entries, _ => false, _ => true);

        Assert.HasCount(1, results);
        Assert.IsTrue(results[0].Result.IsDir);
        Assert.AreEqual("File", results[0].Result.ResultKind);
    }

    [TestMethod]
    public void Collect_ApplicationHistoryUsesApplicationFallbackPresentation()
    {
        var entries = new[] { Entry("word", @"C:\Apps\Word.lnk", HistoryEntryKind.Application) };

        var results = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse("wo"), null, entries, _ => true, _ => false);

        Assert.HasCount(1, results);
        Assert.AreEqual("Word", results[0].Result.Name);
        Assert.AreEqual("Application", results[0].Result.ResultKind);
        Assert.AreEqual(string.Empty, results[0].Result.ParentDir);
        Assert.IsNotNull(results[0].Result.InstantResultOnExecute);
    }

    [TestMethod]
    public void Collect_WslHistoryUsesLexicalPathNormalization()
    {
        var probedPath = string.Empty;
        var entries = new[] { Entry("cache", @"\\wsl$\Ubuntu/home/testuser/~cache/file.txt", HistoryEntryKind.File) };

        var results = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse("cache"), null, entries, path =>
        {
            probedPath = path;
            return true;
        }, _ => false);

        Assert.HasCount(1, results);
        Assert.AreEqual(@"\\wsl$\Ubuntu\home\testuser\~cache\file.txt", probedPath);
        Assert.AreEqual(probedPath, results[0].Result.FullPath);
    }

    [TestMethod]
    public void Collect_PathOutsideScope_DoesNotAddPath()
    {
        var entries = new[] { Entry("bcomp", @"D:\Apps\BCompare.exe", HistoryEntryKind.File) };

        var results = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse("bc"), @"C:\Work", entries, _ => true, _ => false);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void Collect_ScopeItself_DoesNotAddPath()
    {
        var entries = new[] { Entry("work", @"C:\Work", HistoryEntryKind.Folder) };

        var results = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse("work"), @"C:\Work", entries, _ => false, _ => true);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void Collect_SamePathRememberedUnderMultipleKeywords_IsReturnedOnce()
    {
        var entries = new[]
        {
            Entry("bcomp", @"C:\Apps\BCompare.exe", HistoryEntryKind.File),
            Entry("bcompare", @"C:\Apps\BCompare.exe", HistoryEntryKind.File)
        };

        var results = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse("bc"), null, entries, _ => true, _ => false);

        Assert.HasCount(1, results);
    }

    [TestMethod]
    public void MergeRows_LearnedMatchesLeadAndDuplicateOrdinaryRowsAreRemoved()
    {
        var learned = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse("bc"),
            @"C:\Work",
            new[] { Entry("bcomp", @"C:\Work\BCompare.exe", HistoryEntryKind.File) },
            _ => true,
            _ => false);
        var duplicate = new AppSearchResult { FullPath = @"C:\Work\BCompare.exe" };
        var ordinary = new AppSearchResult { FullPath = @"C:\Work\beta.txt" };

        var results = HistorySearchCandidateMapper.MergeRows(learned, new[] { ordinary, duplicate });

        CollectionAssert.AreEqual(
            new[] { @"C:\Work\BCompare.exe", @"C:\Work\beta.txt" },
            results.Select(result => result.FullPath).ToList());
        CollectionAssert.AreEqual(new[] { 0, 1 }, results.Select(result => result.Index).ToList());
    }

    [TestMethod]
    public void ApplyPriorities_LearnedKeywordOverridesGlobalHistoryPriority()
    {
        var learned = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse("bc"),
            null,
            new[] { Entry("bcomp", @"C:\Apps\BCompare.exe", HistoryEntryKind.File) },
            _ => true,
            _ => false);
        var existing = new Dictionary<string, int> { [@"C:\Apps\Other.exe"] = 0 };

        var priorities = HistorySearchCandidateMapper.ApplyPriorities(existing, learned);

        Assert.IsLessThan(priorities[@"C:\Apps\Other.exe"], priorities[@"C:\Apps\BCompare.exe"]);
    }

    [TestMethod]
    public void Collect_UncPathUsesLexicalPathAndChecksExistence()
    {
        var entries = new[] { Entry("report", @"\\remote-server\share\report.docx", HistoryEntryKind.File) };

        var results = HistorySearchCandidateMapper.Collect(FuzzyQuery.Parse("rep"), null, entries, _ => true, _ => false);

        Assert.HasCount(1, results);
        Assert.AreEqual(@"\\remote-server\share\report.docx", results[0].Result.FullPath);
    }

    private static HistoryEntry Entry(string keyword, string path, HistoryEntryKind kind, int count = 1, long time = 100) =>
        new(keyword, path, kind, time, count);
}
