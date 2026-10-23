using System.Globalization;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

[TestClass]
public sealed class RankingEventLogReaderTests
{
    [TestMethod]
    public void Reader_TreatsV2MissingV3FieldsAsUnknown()
    {
        const string line = "{\"v\":2,\"kind\":\"row\",\"rank\":7,\"unexpectedFutureField\":42}";
        Assert.IsTrue(RankingEventLogReader.TryReadLine(line, out var record));
        Assert.IsNull(record.SearchSessionId);
        Assert.IsNull(record.PageId);
        Assert.IsNull(record.FinalDisplayedRank);
        Assert.AreEqual(7, record.KernelRank);
        Assert.AreEqual(RankingCandidateSource.Unknown, record.Source);
        Assert.AreEqual(RankingDropStage.Unknown, record.DropStage);
    }

    [TestMethod]
    public void Reader_IgnoresUnknownFutureFieldsAndEnumValues()
    {
        const string line = "{\"v\":4,\"kind\":\"row\",\"rank\":2,\"kernelRank\":9,\"source\":9999,\"drop\":9999,\"newObject\":{\"x\":1}}";
        Assert.IsTrue(RankingEventLogReader.TryReadLine(line, out var record));
        Assert.AreEqual(2, record.FinalDisplayedRank);
        Assert.AreEqual(9, record.KernelRank);
        Assert.AreEqual(RankingCandidateSource.Unknown, record.Source);
        Assert.AreEqual(RankingDropStage.Unknown, record.DropStage);
    }

    [TestMethod]
    public void Reader_MalformedOrNonObjectLine_FailsWithoutThrowing()
    {
        Assert.IsFalse(RankingEventLogReader.TryReadLine("not-json", out _));
        Assert.IsFalse(RankingEventLogReader.TryReadLine("[]", out _));
    }

    [TestMethod]
    public void Reader_PreservesKnownCombinedSourceFlags()
    {
        const string line = "{\"v\":3,\"kind\":\"row\",\"source\":9}";
        Assert.IsTrue(RankingEventLogReader.TryReadLine(line, out var record));
        Assert.AreEqual(RankingCandidateSource.FileIndex | RankingCandidateSource.History, record.Source);
    }

    [TestMethod]
    public void ExplanationFormatter_UsesInvariantStableKeys_NotLocalizedUiText()
    {
        var session = RankingEventLog.BeginSession("raw", "canonical");
        var page = new RankingPageStamp(session.SearchSessionId, "p-test", RankingTraceWindow.Quick, session.QuerySequence);
        var ranked = Row(@"D:\atlas.txt", 3);
        var row = new RankingDisplayedRow(ranked.Path, 5, ranked.Source,
            RankingSelectionStage.TypeCoverage, null, RankingProviderState.Unknown, ranked);
        var explanation = new RankingExplanation(session, page, default, row);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var text = RankingExplanationFormatter.Format(explanation, "raw", "Atlas", ranked.Path);
            StringAssert.Contains(text, $"rankingPolicyVersion={RankPolicy.Version}");
            StringAssert.Contains(text, "result.finalDisplayedRank=5");
            StringAssert.Contains(text, "result.selectionStage=type-coverage");
            StringAssert.Contains(text, "rank.kernel=3");
            StringAssert.Contains(text, "rank.matchCoverage=0");
            Assert.IsFalse(text.Contains("Action_", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static RankingLogRow Row(string path, int kernelRank) => new(
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
        RankingCandidateSource.FileIndex);
}
