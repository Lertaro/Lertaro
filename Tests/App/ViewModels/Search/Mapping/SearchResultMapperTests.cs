using System.IO;
using Lertaro.Core;
using Lertaro.App.ViewModels.Search.Mapping;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class RankAndDedupeTests
{
    private static AppSearchResult Result(string path) => new() { FullPath = path, Name = path };

    private static SearchResultMapper.RankedCandidate Candidate(
        string path,
        bool isCurated = false,
        int priority = int.MaxValue,
        int typeRank = int.MaxValue,
        double weight = 0,
        string? normalizedPath = null) =>
        new(Result(path), isCurated, priority, typeRank, weight, normalizedPath ?? path);

    [TestMethod]
    public void RankAndDedupe_CuratedBeatsUncuratedRegardlessOfWeight()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\uncurated", isCurated: false, weight: 100),
            Candidate(@"C:\curated", isCurated: true, weight: 0),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\curated", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_AmongCurated_LowerPriorityValueWinsFirst()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\second", isCurated: true, priority: 5),
            Candidate(@"C:\first", isCurated: true, priority: 1),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\first", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_SamePriority_LowerTypeRankWinsNext()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\typeB", typeRank: 2),
            Candidate(@"C:\typeA", typeRank: 1),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\typeA", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_SameTypeRank_HigherWeightWinsNext()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\lowWeight", weight: 0.3),
            Candidate(@"C:\highWeight", weight: 0.9),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\highWeight", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_SameWeight_ShorterNormalizedPathWinsNext()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\a\much\longer\path.txt", weight: 0.5, normalizedPath: @"C:\a\much\longer\path.txt"),
            Candidate(@"C:\short.txt", weight: 0.5, normalizedPath: @"C:\short.txt"),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\short.txt", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_AllTiedExceptPath_SortsAlphabetically()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\zebra.txt", normalizedPath: @"C:\zebra.txt"),
            Candidate(@"C:\apple.txt", normalizedPath: @"C:\apple.txt"),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\apple.txt", ranked[0].FullPath);
        Assert.AreEqual(@"C:\zebra.txt", ranked[1].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_AlphabeticalTiebreakIsCaseInsensitive()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\Bravo.txt", normalizedPath: @"C:\Bravo.txt"),
            Candidate(@"C:\alpha.txt", normalizedPath: @"C:\alpha.txt"),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\alpha.txt", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_DuplicateNormalizedPath_KeepsOnlyHigherRankedOne()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\dup-weak", isCurated: false, normalizedPath: @"C:\same"),
            Candidate(@"C:\dup-strong", isCurated: true, normalizedPath: @"C:\same"),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.HasCount(1, ranked);
        Assert.AreEqual(@"C:\dup-strong", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_DuplicateNormalizedPathIsCaseInsensitive()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\a", normalizedPath: @"C:\SAME"),
            Candidate(@"C:\b", normalizedPath: @"C:\same"),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.HasCount(1, ranked);
    }

    [TestMethod]
    public void RankAndDedupe_EmptyInput_ReturnsEmptyList() =>
        Assert.IsEmpty(SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>()));

    [TestMethod]
    public void RankAndDedupe_FullPriorityChain_OrdersByEachTierInTurn()
    {
        // curated+lowest-priority should win even though it has the worst weight and longest path.
        var winner = Candidate(@"C:\winner-very-long-path-name.txt", isCurated: true, priority: 0, typeRank: 5, weight: 0.1, normalizedPath: @"C:\winner-very-long-path-name.txt");
        var loser = Candidate(@"C:\z.txt", isCurated: false, priority: 0, typeRank: 0, weight: 1.0, normalizedPath: @"C:\z.txt");

        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate> { loser, winner });

        Assert.AreEqual(winner.Result.FullPath, ranked[0].FullPath);
    }
}

[TestClass]
public sealed class DirectorySelfExclusionTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    private static SearchResult Result(string path) => new() { Name = Path.GetFileName(path), Path = path };

    [TestMethod]
    public void RemoveQueriedDirectoryItself_DriveRootQuery_RemovesMatchingEntry()
    {
        var results = new List<SearchResult> { Result(@"C:\"), Result(@"C:\other.txt") };

        SearchResultMapper.RemoveQueriedDirectoryItself(results, @"C:\");

        Assert.HasCount(1, results);
        Assert.AreEqual(@"C:\other.txt", results[0].Path);
    }

    [TestMethod]
    public void RemoveQueriedDirectoryItself_PlainFileNameQuery_RemovesNothing()
    {
        var results = new List<SearchResult> { Result(@"C:\report.txt") };

        SearchResultMapper.RemoveQueriedDirectoryItself(results, "report");

        Assert.HasCount(1, results);
    }

    [TestMethod]
    public void RemoveQueriedDirectoryItself_ExistingDirectoryWithTrailingSeparator_RemovesMatchingEntry()
    {
        using var dir = new TempDirectory();
        var results = new List<SearchResult> { Result(dir.Path) };

        SearchResultMapper.RemoveQueriedDirectoryItself(results, dir.Path + @"\");

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void RemoveQueriedDirectoryItself_NonExistentPathWithTrailingSeparator_RemovesNothing()
    {
        var results = new List<SearchResult> { Result(@"Z:\definitely-not-real-lertaro-dir\") };

        SearchResultMapper.RemoveQueriedDirectoryItself(results, @"Z:\definitely-not-real-lertaro-dir\");

        Assert.HasCount(1, results);
    }

    [TestMethod]
    public void RemoveQueriedDirectoryItself_NullResults_DoesNotThrow() =>
        SearchResultMapper.RemoveQueriedDirectoryItself(null, @"C:\");

    [TestMethod]
    public void RemoveQueriedDirectoryItself_EmptyQuery_RemovesNothing()
    {
        var results = new List<SearchResult> { Result(@"C:\a.txt") };

        SearchResultMapper.RemoveQueriedDirectoryItself(results, "");

        Assert.HasCount(1, results);
    }

    [TestMethod]
    public void IsQueriedDirectoryItself_DriveRootQueryMatchingPath_ReturnsTrue() =>
        Assert.IsTrue(SearchResultMapper.IsQueriedDirectoryItself(@"C:\", @"C:\"));

    [TestMethod]
    public void IsQueriedDirectoryItself_WslDirectoryQuery_ReturnsTrue() =>
        Assert.IsTrue(SearchResultMapper.IsQueriedDirectoryItself(
            @"\\wsl$\Ubuntu\home\testuser", @"\\wsl$\Ubuntu\home\testuser\"));

    [TestMethod]
    public void IsQueriedDirectoryItself_NonMatchingPath_ReturnsFalse() =>
        Assert.IsFalse(SearchResultMapper.IsQueriedDirectoryItself(@"C:\other.txt", @"C:\"));

    [TestMethod]
    public void IsQueriedDirectoryItself_PlainFileQuery_ReturnsFalse() =>
        Assert.IsFalse(SearchResultMapper.IsQueriedDirectoryItself(@"C:\report.txt", "report"));
}
