using System.Text.Json;
using System.Text.RegularExpressions;

using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

// Uses process-wide logger state.
[TestClass]
[DoNotParallelize]
public sealed class RankingEventLogTests
{
    private string _dir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LertaroRankingLog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        RankingEventLog.DirectoryOverride = _dir;
        RankingEventLog.EnabledOverride = true;
    }

    [TestCleanup]
    public void TearDown()
    {
        RankingEventLog.DirectoryOverride = null;
        RankingEventLog.EnabledOverride = null;
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string MainPath => Path.Combine(_dir, "ranking-events.jsonl");
    private string Read() => File.ReadAllText(MainPath);

    private static RankingLogRow Row(string path, int kernelRank, RankingCandidateSource source = RankingCandidateSource.FileIndex) =>
        new(
            path,
            kernelRank,
            RankSection.Boosted,
            18,
            0,
            IndexRuleEffect.Normal,
            HistoryTier.None,
            0,
            MatchRank.NoMatch,
            false,
            false,
            false,
            false,
            source);

    private static RankingLogRow[] Rows(int count) =>
        Enumerable.Range(1, count).Select(i => Row($@"D:\page\file{i}.txt", i)).ToArray();

    private static RankingPageTrace Trace(
        RankingLogSession session,
        IReadOnlyList<RankingDisplayedRow> displayed,
        bool final = true,
        IReadOnlyList<RankingDroppedCandidate>? dropped = null,
        IReadOnlyList<RankingProviderTrace>? providers = null) =>
        new(
            session,
            new RankingPageCounts(12, 11, 10, 9, 1, 1, 10, displayed.Count),
            displayed,
            dropped ?? Array.Empty<RankingDroppedCandidate>(),
            providers ?? Array.Empty<RankingProviderTrace>(),
            ["__builtin::Files", "CoreExtensions.StartMenu"],
            final);

    private JsonElement[] Events(string kind) =>
        File.ReadAllLines(MainPath)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .Where(element => element.GetProperty("kind").GetString() == kind)
            .ToArray();

    [TestMethod]
    public void RecordQuery_WritesHashesAndJoinIdsInsteadOfText()
    {
        RankingEventLog.RecordQuery("secret-query", [Row(@"C:\secret\place\file.txt", 1)]);

        var text = Read();
        Assert.IsTrue(text.Contains("\"finalDisplayedRank\":1"), text);
        Assert.IsTrue(text.Contains("\"kernelRank\":1"), text);
        Assert.IsTrue(text.Contains("\"bucket\""), text);
        Assert.IsTrue(text.Contains("\"opened\":false"), text);
        Assert.IsTrue(text.Contains($"\"policyVersion\":{RankPolicy.Version}"), text);
        Assert.IsFalse(text.Contains("secret-query"), text);
        Assert.IsFalse(text.Contains("file.txt"), text);

        var page = Events("page")[0];
        var row = Events("row")[0];
        Assert.AreEqual(RankingEventLog.SchemaVersion, page.GetProperty("v").GetInt32());
        Assert.AreEqual(page.GetProperty("pageId").GetString(), row.GetProperty("pageId").GetString());
        Assert.AreEqual(page.GetProperty("searchSessionId").GetString(), row.GetProperty("searchSessionId").GetString());
        Assert.IsTrue(Regex.IsMatch(page.GetProperty("queryHash").GetString()!, "^[0-9a-f]{12}$"));
    }

    [TestMethod]
    public void RecordPage_UsesFinalRankAfterPluginPrefix_NotKernelRank()
    {
        var session = RankingEventLog.BeginSession("atlas", "atlas");
        var ranked = Row(@"D:\page\atlas.txt", 1);
        var displayed = new[]
        {
            new RankingDisplayedRow("__PLUGIN_ACTION__:7", 1, RankingCandidateSource.PluginAction,
                RankingSelectionStage.PluginPrefix, "Commands", RankingProviderState.ReadyWithItems, null),
            new RankingDisplayedRow(ranked.Path, 2, ranked.Source,
                RankingSelectionStage.KernelOrder, null, RankingProviderState.Unknown, ranked),
        };

        _ = RankingEventLog.RecordPage(Trace(session, displayed));

        var rows = Events("row");
        Assert.AreEqual(1, rows[0].GetProperty("finalDisplayedRank").GetInt32());
        Assert.AreEqual(-1, rows[0].GetProperty("score").GetInt32());
        Assert.AreEqual(2, rows[1].GetProperty("finalDisplayedRank").GetInt32());
        Assert.AreEqual(1, rows[1].GetProperty("kernelRank").GetInt32());
        Assert.AreEqual((int)RankingSelectionStage.PluginPrefix, rows[0].GetProperty("selectionStage").GetInt32());
    }

    [TestMethod]
    public void PageRowAndOpen_JoinExactlyBySessionPagePathAndFinalRank()
    {
        const string path = @"D:\page\atlas.txt";
        var session = RankingEventLog.BeginSession("raw :token", "raw");
        var ranked = Row(path, 17);
        var stamp = RankingEventLog.RecordPage(Trace(session,
            [new RankingDisplayedRow(path, 4, ranked.Source, RankingSelectionStage.TokenOrder, null,
                RankingProviderState.Unknown, ranked)]));
        Assert.IsNotNull(stamp);

        RankingEventLog.RecordOpened("raw", path, new RankingImpression(stamp.Value, 4));

        var page = Events("page")[0];
        var row = Events("row")[0];
        var opened = Events("open")[0];
        foreach (var property in new[] { "searchSessionId", "pageId", "pathHash" })
            Assert.AreEqual(row.GetProperty(property).GetString(), opened.GetProperty(property).GetString(), property);
        Assert.AreEqual(page.GetProperty("pageId").GetString(), opened.GetProperty("pageId").GetString());
        Assert.AreEqual(4, opened.GetProperty("finalDisplayedRank").GetInt32());
        Assert.AreEqual(17, row.GetProperty("kernelRank").GetInt32());
    }

    [TestMethod]
    public void RecordQuery_RecordsEveryFinallyDisplayedRow()
    {
        RankingEventLog.RecordQuery("atlas", Rows(12));

        var ranks = Events("row").Select(element => element.GetProperty("finalDisplayedRank").GetInt32()).ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(1, 12).ToArray(), ranks);
    }

    [TestMethod]
    public void RecordPage_WritesStageCountsTypeOrderAndProviderReadiness()
    {
        var session = RankingEventLog.BeginSession("atlas", "atlas");
        _ = RankingEventLog.RecordPage(Trace(
            session,
            [new RankingDisplayedRow(@"D:\a", 1, RankingCandidateSource.FileIndex,
                RankingSelectionStage.KernelOrder, null, RankingProviderState.Unknown, Row(@"D:\a", 1))],
            providers: [new RankingProviderTrace("StartMenu", RankingProviderState.Loading, 0, 0)]));

        var page = Events("page")[0];
        Assert.AreEqual(12, page.GetProperty("engineCandidates").GetInt32());
        Assert.AreEqual(10, page.GetProperty("preMergeCount").GetInt32());
        Assert.AreEqual(9, page.GetProperty("postMergeCount").GetInt32());
        Assert.AreEqual(1, page.GetProperty("prefixCount").GetInt32());
        Assert.AreEqual("__builtin::Files", page.GetProperty("order")[0].GetString());
        Assert.AreEqual("StartMenu", page.GetProperty("providers")[0].GetProperty("id").GetString());
        Assert.AreEqual(0, page.GetProperty("providers")[0].GetProperty("ready").GetInt32());
    }

    [TestMethod]
    public void RecordQuery_DecomposesTheKeyWithoutNamingAnything()
    {
        var path = @"D:\tools\smalltools\Auslogics Duplicate File Finder.lnk";
        var row = new RankingLogRow(
            path, 1, RankSection.Boosted, 34, 0, IndexRuleEffect.Normal, HistoryTier.H5, 5000,
            new MatchRank(MatchRank.TierName, 0, 0.742, 1), false, false, false, false,
            RankingCandidateSource.FileIndex | RankingCandidateSource.History);
        RankingEventLog.RecordQuery("auslogics", [row]);

        var element = Events("row")[0];
        Assert.AreEqual(34, element.GetProperty("score").GetInt32());
        Assert.AreEqual((int)HistoryTier.H5, element.GetProperty("hTier").GetInt32());
        Assert.AreEqual(742, element.GetProperty("mCov").GetInt32());
        Assert.AreEqual(1000, element.GetProperty("mCont").GetInt32());
        Assert.AreEqual(1, element.GetProperty("ext").GetInt32());
        Assert.AreEqual(4, element.GetProperty("depth").GetInt32());
        Assert.AreEqual(1, element.GetProperty("merged").GetInt32());
        Assert.IsFalse(Read().Contains("Auslogics", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RecordQuery_WritesShadowEvidenceAndKindsWithoutRawIdentityText()
    {
        const string path = @"D:\Apps\PixPin.lnk";
        var rank = new MatchRank(MatchRank.TierName, 0, 1);
        var row = Row(path, 1, RankingCandidateSource.SearchableProvider) with
        {
            Match = rank,
            IsApplication = true,
            Evidence = MatchEvidence.Accepted(
                "pixpin", "PixPin", MatchEvidenceField.DisplayTitle, rank, providerId: "StartMenu"),
            Identity = CandidateIdentity.Create(
                RankingCandidateSource.SearchableProvider, false, true, "PixPin private title", path),
        };

        RankingEventLog.RecordQuery("pixpin", [row]);

        var element = Events("row")[0];
        Assert.AreEqual((int)MatchEvidenceField.DisplayTitle, element.GetProperty("field").GetInt32());
        Assert.AreEqual((int)MatchEvidenceMode.LegacyUnknown, element.GetProperty("mode").GetInt32());
        Assert.AreEqual((int)MatchEvidenceGrade.ExactIdentity, element.GetProperty("evidenceGrade").GetInt32());
        Assert.AreEqual(
            (int)(RankingCandidateKind.File | RankingCandidateKind.Application | RankingCandidateKind.Shortcut),
            element.GetProperty("kindFlags").GetInt32());
        Assert.IsFalse(Read().Contains("PixPin private title", StringComparison.Ordinal));
        Assert.IsFalse(Read().Contains(path, StringComparison.Ordinal));
    }

    [TestMethod]
    public void SameStreamingPageWithinFiveSeconds_CollapsesAndReturnsExistingPageId()
    {
        var session = RankingEventLog.BeginSession("atlas", "atlas");
        var row = Row(@"D:\page\file.txt", 1);
        var trace = Trace(session,
            [new RankingDisplayedRow(row.Path, 1, row.Source, RankingSelectionStage.KernelOrder,
                null, RankingProviderState.Unknown, row)],
            final: false);

        var first = RankingEventLog.RecordPage(trace);
        var second = RankingEventLog.RecordPage(trace);

        Assert.AreEqual(first, second);
        Assert.AreEqual(2, File.ReadAllLines(MainPath).Length, "one page plus one displayed row");
    }

    [TestMethod]
    public void SameRowsWithChangedDiagnostics_AppendNewPage()
    {
        var session = RankingEventLog.BeginSession("atlas", "atlas");
        var row = Row(@"D:\page\file.txt", 1);
        RankingPageTrace Page(RankingLogRow ranked) => Trace(session,
            [new RankingDisplayedRow(ranked.Path, 1, ranked.Source, RankingSelectionStage.KernelOrder,
                null, RankingProviderState.Unknown, ranked)], final: false);

        var first = RankingEventLog.RecordPage(Page(row));
        var second = RankingEventLog.RecordPage(Page(row with { Score = row.Score + 1 }));

        Assert.AreNotEqual(first, second);
        Assert.AreEqual(4, File.ReadAllLines(MainPath).Length);
    }

    [TestMethod]
    public void DifferentStreamingRows_AppendNewPage()
    {
        var session = RankingEventLog.BeginSession("atlas", "atlas");
        foreach (var name in new[] { "a", "b" })
        {
            var row = Row($@"D:\{name}.txt", 1);
            _ = RankingEventLog.RecordPage(Trace(session,
                [new RankingDisplayedRow(row.Path, 1, row.Source, RankingSelectionStage.KernelOrder,
                    null, RankingProviderState.Unknown, row)], final: false));
        }
        Assert.AreEqual(4, File.ReadAllLines(MainPath).Length);
        Assert.AreNotEqual(Events("page")[0].GetProperty("pageId").GetString(),
            Events("page")[1].GetProperty("pageId").GetString());
    }

    [TestMethod]
    public void FinalPage_WritesDroppedCandidateStage()
    {
        var session = RankingEventLog.BeginSession("atlas", "atlas");
        var row = Row(@"D:\lost.txt", 73, RankingCandidateSource.SearchableProvider);
        _ = RankingEventLog.RecordPage(Trace(session, Array.Empty<RankingDisplayedRow>(), dropped:
            [new RankingDroppedCandidate(row.Path, row.Source, "StartMenu", RankingProviderState.ReadyWithItems,
                row.KernelRank, RankingDropStage.FinalDisplayCap, row)]));

        var candidate = Events("candidate")[0];
        Assert.AreEqual((int)RankingDropStage.FinalDisplayCap, candidate.GetProperty("drop").GetInt32());
        Assert.AreEqual(73, candidate.GetProperty("kernelRank").GetInt32());
        Assert.AreEqual(0, candidate.GetProperty("displayed").GetInt32());
        Assert.IsFalse(Read().Contains("lost.txt", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Disabled_WritesNothing()
    {
        RankingEventLog.EnabledOverride = false;
        RankingEventLog.RecordQuery("atlas", Rows(3));
        RankingEventLog.RecordOpened("atlas", @"D:\page\file1.txt");
        Assert.IsFalse(File.Exists(MainPath));
    }

    [TestMethod]
    public void LegacyOpenWithoutImpression_HonestlyOmitsJoinFields()
    {
        RankingEventLog.RecordOpened("atlas", @"D:\page\file1.txt");
        var opened = Events("open")[0];
        Assert.IsFalse(opened.TryGetProperty("pageId", out _));
        Assert.IsFalse(opened.TryGetProperty("finalDisplayedRank", out _));
        Assert.IsFalse(Read().Contains("file1", StringComparison.Ordinal));
    }

    [TestMethod]
    public void V3Writer_AppendsWithoutRewritingExistingV2Line()
    {
        const string legacy = "{\"v\":2,\"kind\":\"open\",\"queryHash\":\"abc\"}";
        File.WriteAllText(MainPath, legacy + Environment.NewLine);

        RankingEventLog.RecordQuery("atlas", [Row(@"D:\a.txt", 1)]);

        Assert.AreEqual(legacy, File.ReadLines(MainPath).First());
        Assert.AreEqual(3, File.ReadAllLines(MainPath).Length);
    }

}
