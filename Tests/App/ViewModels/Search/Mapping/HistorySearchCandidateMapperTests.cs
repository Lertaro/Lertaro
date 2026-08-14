using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class HistorySearchCandidateMapperTests
{
    [TestMethod]
    public void Collect_QueryMatchesRecordedKeyword_AddsExistingPathWithLearnedPriority()
    {
        var entries = new[] { Entry("bcomp", @"C:\Apps\BCompare.exe", HistoryEntryKind.File) };

        var results = HistorySearchCandidateMapper.Collect("bc", null, entries, _ => true, _ => false);

        Assert.HasCount(1, results);
        Assert.AreEqual(@"C:\Apps\BCompare.exe", results[0].Result.FullPath);
        Assert.IsTrue(results[0].IsCurated);
        Assert.IsLessThan(0, results[0].Priority);
    }

    [TestMethod]
    public void Collect_UnrelatedKeyword_DoesNotAddPath()
    {
        var entries = new[] { Entry("other", @"C:\Apps\BCompare.exe", HistoryEntryKind.File) };

        var results = HistorySearchCandidateMapper.Collect("bc", null, entries, _ => true, _ => false);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void Collect_MissingPath_DoesNotAddPath()
    {
        var entries = new[] { Entry("bcomp", @"C:\Apps\BCompare.exe", HistoryEntryKind.File) };

        var results = HistorySearchCandidateMapper.Collect("bc", null, entries, _ => false, _ => false);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void Collect_FolderHistory_CreatesDirectoryResult()
    {
        var entries = new[] { Entry("proj", @"C:\Work\Project", HistoryEntryKind.Folder) };

        var results = HistorySearchCandidateMapper.Collect("pro", null, entries, _ => false, _ => true);

        Assert.HasCount(1, results);
        Assert.IsTrue(results[0].Result.IsDir);
        Assert.AreEqual("File", results[0].Result.ResultKind);
    }

    [TestMethod]
    public void Collect_ApplicationHistoryUsesApplicationFallbackPresentation()
    {
        var entries = new[] { Entry("word", @"C:\Apps\Word.lnk", HistoryEntryKind.Application) };

        var results = HistorySearchCandidateMapper.Collect("wo", null, entries, _ => true, _ => false);

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

        var results = HistorySearchCandidateMapper.Collect("cache", null, entries, path =>
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

        var results = HistorySearchCandidateMapper.Collect("bc", @"C:\Work", entries, _ => true, _ => false);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void Collect_ScopeItself_DoesNotAddPath()
    {
        var entries = new[] { Entry("work", @"C:\Work", HistoryEntryKind.Folder) };

        var results = HistorySearchCandidateMapper.Collect("work", @"C:\Work", entries, _ => false, _ => true);

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

        var results = HistorySearchCandidateMapper.Collect("bc", null, entries, _ => true, _ => false);

        Assert.HasCount(1, results);
    }

    [TestMethod]
    public void MergeRows_LearnedMatchesLeadAndDuplicateOrdinaryRowsAreRemoved()
    {
        var learned = HistorySearchCandidateMapper.Collect(
            "bc",
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
        var learned = HistorySearchCandidateMapper.Collect(
            "bc",
            null,
            new[] { Entry("bcomp", @"C:\Apps\BCompare.exe", HistoryEntryKind.File) },
            _ => true,
            _ => false);
        var existing = new Dictionary<string, int> { [@"C:\Apps\Other.exe"] = 0 };

        var priorities = HistorySearchCandidateMapper.ApplyPriorities(existing, learned);

        Assert.IsLessThan(priorities[@"C:\Apps\Other.exe"], priorities[@"C:\Apps\BCompare.exe"]);
    }

    private static HistoryEntry Entry(string keyword, string path, HistoryEntryKind kind) =>
        new(keyword, path, kind, 100);
}
