using Lertaro.App.Services.ShellMenu.Presenter;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
[DoNotParallelize]
public sealed class QuickSearchRankingTraceTests
{
    [TestMethod]
    public void RecordDisplayedPage_StampsFinalRanksIncludingPrefix()
    {
        var first = Candidate(@"D:\first.txt", "first");
        var dropped = Candidate(@"D:\dropped.txt", "dropped");
        var ordered = RankedCandidateOrdering.OrderWithTrace([first, dropped], null, out var pass);
        var prefix = new AppSearchResult
        {
            Name = "Run",
            FullPath = "__PLUGIN_ACTION__:7",
            ResultKind = "PluginAction",
            SearchQuery = "raw"
        };
        var instant = new AppSearchResult
        {
            Name = "Answer",
            FullPath = "__INSTANT__:8",
            ResultKind = "InstantResult",
            SearchQuery = "raw"
        };
        var mapperOutput = new List<AppSearchResult> { prefix, instant };
        mapperOutput.AddRange(ordered.Select(candidate => candidate.Result));

        var trace = new QuickSearchRankingTrace("raw", "raw");
        trace.RegisterMapperOutput(mapperOutput, new MapperTraceSnapshot(
            pass,
            new RankingPageCounts(2, 2, 2, 2, 2, 1, 4, 0),
            Array.Empty<RankingDroppedCandidate>(),
            Array.Empty<RankingProviderTrace>(),
            Array.Empty<string>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ordered[0].NormalizedPath }));

        // Keep both prefix kinds and one kernel row.
        var displayed = new List<AppSearchResult> { prefix, instant, ordered[0].Result };
        trace.RecordDisplayedPage(mapperOutput, displayed, isFinal: true);

        Assert.IsNotNull(prefix.RankingExplanation);
        Assert.AreEqual(1, prefix.RankingExplanation!.Row.FinalDisplayedRank);
        Assert.AreEqual(RankingSelectionStage.PluginPrefix, prefix.RankingExplanation!.Row.SelectionStage);
        Assert.AreEqual(RankingCandidateSource.PluginAction, prefix.RankingExplanation!.Row.Source);

        Assert.IsNotNull(instant.RankingExplanation);
        Assert.AreEqual(2, instant.RankingExplanation!.Row.FinalDisplayedRank);
        Assert.AreEqual(RankingSelectionStage.PluginPrefix, instant.RankingExplanation!.Row.SelectionStage);
        Assert.AreEqual(RankingCandidateSource.InstantProvider, instant.RankingExplanation!.Row.Source);

        Assert.IsNotNull(ordered[0].Result.RankingExplanation);
        Assert.AreEqual(3, ordered[0].Result.RankingExplanation!.Row.FinalDisplayedRank);
        Assert.AreEqual(RankingSelectionStage.TypeCoverage, ordered[0].Result.RankingExplanation!.Row.SelectionStage);
        Assert.AreEqual(prefix.RankingExplanation!.Page.PageId, ordered[0].Result.RankingExplanation!.Page.PageId);
    }

    [TestMethod]
    public void RecordDisplayedPage_TokenPageKeepsOnlyDisplayedExplanation()
    {
        var app = Candidate(@"D:\app.lnk", "app", "Application", RankingCandidateSource.SearchableProvider);
        var filtered = Candidate(@"D:\filtered.txt", "filtered");
        var capped = Candidate(@"D:\capped.txt", "capped");
        var shown = Candidate(@"D:\shown.txt", "shown");
        var ordered = RankedCandidateOrdering.OrderWithTrace([app, filtered, capped, shown], null, out var pass);
        var mapperOutput = ordered.Select(candidate => candidate.Result).ToList();
        AppSearchResult ByPath(string path) => mapperOutput.Single(result => result.FullPath == path);

        var trace = new QuickSearchRankingTrace("raw :token", "raw");
        trace.RegisterMapperOutput(mapperOutput, new MapperTraceSnapshot(
            pass,
            new RankingPageCounts(4, 4, 4, 4, 0, 0, 4, 0),
            Array.Empty<RankingDroppedCandidate>(),
            Array.Empty<RankingProviderTrace>(),
            Array.Empty<string>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

        var tokenInput = new[] { ByPath(filtered.NormalizedPath), ByPath(capped.NormalizedPath), ByPath(shown.NormalizedPath) };
        var tokenOutput = new[] { ByPath(capped.NormalizedPath), ByPath(shown.NormalizedPath) };
        trace.RecordDisplayedPage(
            mapperOutput,
            [ByPath(shown.NormalizedPath)],
            isFinal: true,
            tokenInputRows: tokenInput,
            tokenOutputRows: tokenOutput);

        Assert.IsNotNull(ByPath(shown.NormalizedPath).RankingExplanation);
        Assert.AreEqual(RankingSelectionStage.TokenOrder, ByPath(shown.NormalizedPath).RankingExplanation!.Row.SelectionStage);
        Assert.IsNull(ByPath(capped.NormalizedPath).RankingExplanation);
        Assert.IsNull(ByPath(filtered.NormalizedPath).RankingExplanation);
        Assert.IsNull(ByPath(app.NormalizedPath).RankingExplanation);
    }

    [TestMethod]
    public void BuildText_IncludesEveryDisplayedExplanation()
    {
        var first = Candidate(@"D:\first.txt", "first");
        var second = Candidate(@"D:\second.txt", "second");
        var ordered = RankedCandidateOrdering.OrderWithTrace([first, second], null, out var pass);
        var mapperOutput = ordered.Select(candidate => candidate.Result).ToList();
        var trace = new QuickSearchRankingTrace("raw", "raw");
        trace.RegisterMapperOutput(mapperOutput, new MapperTraceSnapshot(
            pass,
            new RankingPageCounts(2, 2, 2, 2, 2, 0, 2, 0),
            Array.Empty<RankingDroppedCandidate>(),
            Array.Empty<RankingProviderTrace>(),
            Array.Empty<string>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

        trace.RecordDisplayedPage(mapperOutput, mapperOutput, isFinal: true);

        var text = RankingExplanationExport.BuildText(mapperOutput, "raw");

        StringAssert.Contains(text, "resultCount=2");
        StringAssert.Contains(text, "result.path=\"D:\\\\first.txt\"");
        StringAssert.Contains(text, "result.path=\"D:\\\\second.txt\"");
        Assert.AreEqual(3, text.Split("---").Length, "header plus one block per result");
    }

    private static SearchResultMapper.RankedCandidate Candidate(
        string path,
        string name,
        string kind = "File",
        RankingCandidateSource source = RankingCandidateSource.FileIndex)
    {
        var result = new AppSearchResult
        {
            Name = name,
            FullPath = path,
            ParentDir = @"D:\",
            Drive = "D",
            ResultKind = kind,
            SearchQuery = "raw"
        };
        return new SearchResultMapper.RankedCandidate(
            result,
            false,
            0,
            0,
            new MatchRank(MatchRank.TierName, 0, 1, 1),
            path,
            Source: source);
    }
}
