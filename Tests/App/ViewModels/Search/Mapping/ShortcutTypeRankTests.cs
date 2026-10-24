using System.IO;

using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

// A .lnk is a file row and an application shortcut at once, and it ranks as whichever identity the user put
// first (SearchResultTypePriority.ShortcutRank). Before this, a shortcut the app provider never indexed kept
// only the Files rank, so "Shortcut" first in the settings did nothing for it. These cases pin the wiring in
// SearchResultMapper, which used to be reachable only through a real search.
[TestClass]
public sealed class ShortcutTypeRankTests
{
    private const int AppRank = 0;
    private const int FilesRank = 2;

    private static SearchResultMapper.RankedCandidate Candidate(string path, int typeRank, bool isDir = false, string resultKind = "") =>
        new(
            new AppSearchResult { FullPath = path, Name = Path.GetFileName(path), IsDir = isDir, ResultKind = resultKind },
            false,
            0,
            typeRank,
            new MatchRank(MatchRank.TierName, 0, 1.0),
            path);

    private static List<SearchResultMapper.RankedCandidate> Apply(params SearchResultMapper.RankedCandidate[] rows)
    {
        var candidates = rows.ToList();
        SearchResultMapper.ApplyShortcutTypeRank(candidates, FilesRank, AppRank);
        return candidates;
    }

    [TestMethod]
    public void AShortcutFileOnTheFilesRankTakesTheBetterRankNextToTheFolderItCompetedWith()
    {
        var rows = Apply(
            Candidate(@"D:\tools\smalltools\重复文件", FilesRank, isDir: true),
            Candidate(@"D:\tools\smalltools\Auslogics Duplicate File Finder.lnk", FilesRank));

        Assert.AreEqual(FilesRank, rows[0].TypeRank, "a folder keeps the Files rank");
        Assert.AreEqual(AppRank, rows[1].TypeRank, "the shortcut borrows the application rank");
    }

    [TestMethod]
    public void AnApplicationRowAnExecutableAndAWebShortcutKeepTheirOwnRanks()
    {
        var rows = Apply(
            Candidate(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Chrome.lnk", AppRank, resultKind: "Application"),
            Candidate(@"C:\Program Files\Notepad++\notepad++.exe", FilesRank),
            Candidate(@"D:\tools\GitHub.url", FilesRank));

        Assert.AreEqual(AppRank, rows[0].TypeRank, "an indexed .lnk already carries the app rank");
        Assert.AreEqual(FilesRank, rows[1].TypeRank, "an .exe is a file, not an application shortcut");
        Assert.AreEqual(FilesRank, rows[2].TypeRank, "a .url points at a page, so it stays a file row");
    }

    // The mirrored order must be a no-op: min(app, files) is never worse than the row's file identity, so
    // "Files" first cannot demote a shortcut the way an unconditional app-rank override would.
    [TestMethod]
    public void NothingMovesWhenFilesAreRankedFirst()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"D:\tools\a.lnk", AppRank),
            Candidate(@"D:\tools\b", FilesRank, isDir: true),
        };

        SearchResultMapper.ApplyShortcutTypeRank(candidates, FilesRank, FilesRank);

        Assert.AreEqual(AppRank, candidates[0].TypeRank);
        Assert.AreEqual(FilesRank, candidates[1].TypeRank);
    }

    [TestMethod]
    public void NothingMovesWhenTheApplicationTypeIsNotListed()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate> { Candidate(@"D:\tools\a.lnk", int.MaxValue) };

        SearchResultMapper.ApplyShortcutTypeRank(candidates, int.MaxValue, int.MaxValue);

        Assert.AreEqual(int.MaxValue, candidates[0].TypeRank);
    }
}
