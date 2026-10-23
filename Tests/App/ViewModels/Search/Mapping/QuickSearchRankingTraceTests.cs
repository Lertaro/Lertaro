using System.IO;
using System.Text.Json;
using Lertaro.App.Services.ShellMenu.Presenter;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
[DoNotParallelize]
public sealed class QuickSearchRankingTraceTests
{
    private string _dir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LertaroQuickRankingTrace_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        RankingEventLog.DirectoryOverride = _dir;
        RankingEventLog.EnabledOverride = true;
    }

    [TestCleanup]
    public void TearDown()
    {
        RankingEventLog.DirectoryOverride = null;
        RankingEventLog.EnabledOverride = false;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public void RecordDisplayedPage_StampsFinalRanksIncludingPrefix_AndRecordsCapDrop()
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

        Assert.AreEqual(1, prefix.RankingImpression?.FinalDisplayedRank);
        Assert.AreEqual(2, instant.RankingImpression?.FinalDisplayedRank);
        Assert.AreEqual(3, ordered[0].Result.RankingImpression?.FinalDisplayedRank);
        Assert.AreEqual(prefix.RankingImpression?.Page.PageId, ordered[0].Result.RankingImpression?.Page.PageId);

        var events = File.ReadAllLines(Path.Combine(_dir, "ranking-events.jsonl"))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();
        var rows = events.Where(e => e.GetProperty("kind").GetString() == "row").ToArray();
        Assert.AreEqual(3, rows.Length);
        Assert.AreEqual((int)RankingSelectionStage.PluginPrefix, rows[0].GetProperty("selectionStage").GetInt32());
        Assert.AreEqual((int)RankingCandidateSource.PluginAction, rows[0].GetProperty("source").GetInt32());
        Assert.AreEqual((int)RankingSelectionStage.PluginPrefix, rows[1].GetProperty("selectionStage").GetInt32());
        Assert.AreEqual((int)RankingCandidateSource.InstantProvider, rows[1].GetProperty("source").GetInt32());
        Assert.AreEqual((int)RankingSelectionStage.TypeCoverage, rows[2].GetProperty("selectionStage").GetInt32());
        Assert.AreEqual(3, rows[2].GetProperty("finalDisplayedRank").GetInt32());
        var capDrop = events.Single(e => e.GetProperty("kind").GetString() == "candidate");
        Assert.AreEqual((int)RankingDropStage.FinalDisplayCap, capDrop.GetProperty("drop").GetInt32());
    }

    [TestMethod]
    public void ShadowAliasCandidate_IsLoggedWithEvidenceWithoutBecomingARow()
    {
        var rank = new MatchRank(MatchRank.TierName, 0, 1, 1);
        var shadow = Candidate(
            @"D:\Apps\PixPin.lnk",
            "PixPin",
            "Application",
            RankingCandidateSource.SearchableProvider) with
        {
            Match = rank,
            Evidence = MatchEvidence.Accepted(
                "pixpin",
                "PixPin",
                MatchEvidenceField.ShortcutTarget,
                rank,
                variant: MatchQueryVariant.ProviderAlias),
        };
        var ordered = RankedCandidateOrdering.OrderWithTrace([], null, out var pass);
        var mapperOutput = ordered.Select(candidate => candidate.Result).ToList();
        var trace = new QuickSearchRankingTrace("pixpin", "pixpin");
        trace.RegisterMapperOutput(mapperOutput, new MapperTraceSnapshot(
            pass,
            default,
            Array.Empty<RankingDroppedCandidate>(),
            Array.Empty<RankingProviderTrace>(),
            Array.Empty<string>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new[] { shadow }));

        trace.RecordDisplayedPage(mapperOutput, [], isFinal: true);

        var logPath = Path.Combine(_dir, "ranking-events.jsonl");
        var logText = File.ReadAllText(logPath);
        Assert.DoesNotContain("PixPin", logText, StringComparison.Ordinal);
        var events = File.ReadAllLines(logPath)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();
        Assert.IsEmpty(events.Where(element => element.GetProperty("kind").GetString() == "row"));
        var candidate = events.Single(element => element.GetProperty("kind").GetString() == "candidate");
        Assert.AreEqual((int)RankingDropStage.ShadowAliasGate, candidate.GetProperty("drop").GetInt32());
        Assert.AreEqual((int)MatchEvidenceField.ShortcutTarget, candidate.GetProperty("field").GetInt32());
        Assert.AreEqual((int)MatchQueryVariant.ProviderAlias, candidate.GetProperty("queryVariant").GetInt32());
        Assert.AreEqual((int)MatchEvidenceGrade.SecondaryField, candidate.GetProperty("evidenceGrade").GetInt32());
        Assert.AreEqual(-1, candidate.GetProperty("kernelRank").GetInt32());
    }

    [TestMethod]
    public void TokenComposition_SeparatesDomainFilterTokenFilterAndFinalCap()
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

        var candidates = File.ReadAllLines(Path.Combine(_dir, "ranking-events.jsonl"))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .Where(element => element.GetProperty("kind").GetString() == "candidate")
            .ToDictionary(element => element.GetProperty("pathHash").GetString()!,
                element => element.GetProperty("drop").GetInt32());
        Assert.AreEqual((int)RankingDropStage.TokenDomainFilter,
            candidates[RankingEventLog.HashKey(app.NormalizedPath)]);
        Assert.AreEqual((int)RankingDropStage.TokenFilter,
            candidates[RankingEventLog.HashKey(filtered.NormalizedPath)]);
        Assert.AreEqual((int)RankingDropStage.FinalDisplayCap,
            candidates[RankingEventLog.HashKey(capped.NormalizedPath)]);
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
