using System.IO;

using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using PriorityVerdict = Lertaro.Core.ExclusionRuleSet.PriorityVerdict;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

// Folders are their own entry in the result-type order, so "Applications, Files, Folders" can be said. Every
// file-backed source puts its rows on the Files rank first; these cases pin the pass that moves directories
// to the Folders rank and the trigger rule that splits the two buckets.
[TestClass]
public sealed class FolderTypeRankTests
{
    private const int AppRank = 0;
    private const int FilesRank = 1;
    private const int FoldersRank = 2;

    private static SearchResultMapper.RankedCandidate Candidate(
        string path, int typeRank, bool isDir = false, string resultKind = "", double coverage = 1.0) =>
        new(
            new AppSearchResult { FullPath = path, Name = Path.GetFileName(path), IsDir = isDir, ResultKind = resultKind },
            false,
            0,
            typeRank,
            new MatchRank(MatchRank.TierName, 0, coverage, 1.0, MatchRank.ShapeWholeWord),
            path);

    [TestMethod]
    public void ADirectoryOnTheFilesRankMovesToTheFoldersRank()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"D:\games\wand_gfx", FilesRank, isDir: true),
            Candidate(@"D:\games\wand_1.png", FilesRank),
            Candidate(@"D:\Start Menu\Wand", AppRank, isDir: true, resultKind: "Application"),
        };

        SearchResultMapper.ApplyFolderTypeRank(candidates, FilesRank, FoldersRank);

        Assert.AreEqual(FoldersRank, candidates[0].TypeRank);
        Assert.IsTrue(candidates[0].TypeRankApplies);
        Assert.AreEqual(FilesRank, candidates[1].TypeRank, "a file stays in the Files bucket");
        Assert.AreEqual(AppRank, candidates[2].TypeRank, "a row on another type's rank keeps it");
    }

    [TestMethod]
    public void NothingMovesWhileBothFileBucketsAreUnlisted()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate> { Candidate(@"D:\a", int.MaxValue, isDir: true) };

        SearchResultMapper.ApplyFolderTypeRank(candidates, int.MaxValue, int.MaxValue);

        Assert.AreEqual(int.MaxValue, candidates[0].TypeRank);
    }

    // The point of the feature: at the same placement, Files above Folders puts the file first even though
    // the folder's shorter name covers more of it -- coverage comes after the type order.
    [TestMethod]
    public void FilesAboveFoldersPutsTheFileFirstAtTheSamePlacement()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"D:\games\wand_gfx", FilesRank, isDir: true, coverage: 0.5),
            Candidate(@"D:\games\wand_1.png", FilesRank, coverage: 0.4),
        };
        var context = new RankContext((_, _) => new PriorityVerdict(IndexRuleEffect.Normal, false));

        var together = RankedCandidateOrdering.Order(candidates, context);
        SearchResultMapper.ApplyFolderTypeRank(candidates, FilesRank, FoldersRank);
        var split = RankedCandidateOrdering.Order(candidates, context);

        Assert.AreEqual("wand_gfx", together[0].Result.Name, "precondition: in one bucket, coverage decides");
        Assert.AreEqual("wand_1.png", split[0].Result.Name);
    }

    [TestMethod]
    public void ATypeTriggerKeepsOnlyTheFileIndexRowsOfItsOwnBucket()
    {
        Assert.IsTrue(SearchResultMapper.InTriggeredFileBucket(isDir: false, null));
        Assert.IsTrue(SearchResultMapper.InTriggeredFileBucket(isDir: true, null));
        Assert.IsTrue(SearchResultMapper.InTriggeredFileBucket(isDir: false, SearchResultTypePriority.FilesTypeId));
        Assert.IsFalse(SearchResultMapper.InTriggeredFileBucket(isDir: true, SearchResultTypePriority.FilesTypeId));
        Assert.IsTrue(SearchResultMapper.InTriggeredFileBucket(isDir: true, SearchResultTypePriority.FoldersTypeId));
        Assert.IsFalse(SearchResultMapper.InTriggeredFileBucket(isDir: false, SearchResultTypePriority.FoldersTypeId));
        Assert.IsFalse(SearchResultMapper.InTriggeredFileBucket(isDir: true, "apps"));
        Assert.IsFalse(SearchResultMapper.InTriggeredFileBucket(isDir: false, "apps"));
    }
}
