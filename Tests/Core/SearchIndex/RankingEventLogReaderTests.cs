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
        Assert.AreEqual(MatchEvidenceField.LegacyUnknown, record.EvidenceField);
        Assert.AreEqual(MatchEvidenceMode.LegacyUnknown, record.EvidenceMode);
        Assert.AreEqual(MatchEvidenceGrade.LegacyUnknown, record.EvidenceGrade);
        Assert.AreEqual(RankingCandidateKind.Unknown, record.Kind);
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
    public void Reader_PreservesSchema4ShadowAliasProvenance()
    {
        var line = $"{{\"v\":4,\"kind\":\"candidate\",\"drop\":{(int)RankingDropStage.ShadowAliasGate}," +
                   $"\"field\":{(int)MatchEvidenceField.ProductMetadata}," +
                   $"\"queryVariant\":{(int)MatchQueryVariant.ProviderAlias}," +
                   $"\"evidenceGrade\":{(int)MatchEvidenceGrade.SecondaryField}}}";

        Assert.IsTrue(RankingEventLogReader.TryReadLine(line, out var record));
        Assert.AreEqual(RankingDropStage.ShadowAliasGate, record.DropStage);
        Assert.AreEqual(MatchEvidenceField.ProductMetadata, record.EvidenceField);
        Assert.AreEqual(MatchQueryVariant.ProviderAlias, record.QueryVariant);
        Assert.AreEqual(MatchEvidenceGrade.SecondaryField, record.EvidenceGrade);
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
    public void Reader_PreservesKnownEvidenceAndCombinedKindFlags()
    {
        const string line = "{\"v\":3,\"kind\":\"row\",\"field\":1,\"mode\":0,\"queryVariant\":1,\"evidenceGrade\":1,\"kindFlags\":14}";
        Assert.IsTrue(RankingEventLogReader.TryReadLine(line, out var record));
        Assert.AreEqual(MatchEvidenceField.DisplayTitle, record.EvidenceField);
        Assert.AreEqual(MatchEvidenceMode.LegacyUnknown, record.EvidenceMode);
        Assert.AreEqual(MatchQueryVariant.Original, record.QueryVariant);
        Assert.AreEqual(MatchEvidenceGrade.ExactIdentity, record.EvidenceGrade);
        Assert.AreEqual(
            RankingCandidateKind.File | RankingCandidateKind.Application | RankingCandidateKind.Shortcut,
            record.Kind);
    }

    [TestMethod]
    public void ExplanationFormatter_UsesInvariantStableKeys_NotLocalizedUiText()
    {
        var session = RankingEventLog.BeginSession("raw", "canonical");
        var page = new RankingPageStamp(session.SearchSessionId, "p-test", RankingTraceWindow.Quick, session.QuerySequence);
        var ranked = Row(@"D:\atlas.txt", 3) with
        {
            Evidence = MatchEvidence.Accepted(
                "atlas", "atlas.txt", MatchEvidenceField.FileName,
                new MatchRank(MatchRank.TierName, 0, 1)),
            Identity = CandidateIdentity.Create(
                RankingCandidateSource.FileIndex, false, false, "atlas.txt", @"D:\atlas.txt"),
        };
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
            StringAssert.Contains(text, "evidence.field=FileName");
            StringAssert.Contains(text, "evidence.grade=ExactIdentity");
            StringAssert.Contains(text, "evidence.mode=LegacyUnknown");
            StringAssert.Contains(text, "candidate.kindFlags=File");
            Assert.IsFalse(text.Contains("Action_", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    public void ReaderAndFormatter_PreserveShadowV3RankAndDelta()
    {
        var session = RankingEventLog.BeginSession("raw", "canonical");
        var page = new RankingPageStamp(session.SearchSessionId, "p-shadow", RankingTraceWindow.Quick, session.QuerySequence);
        var ranked = Row(@"D:\pixpin.lnk", 4) with
        {
            ShadowV3Rank = 1,
            ShadowV3RankDelta = 3,
        };
        var displayed = new RankingDisplayedRow(
            ranked.Path, 4, RankingCandidateSource.SearchableProvider, RankingSelectionStage.KernelOrder,
            null, RankingProviderState.ReadyWithItems, ranked);
        var json = RankingEventJsonWriter.DisplayedRowJson(1700000000000, page, session, "deadbeefcafe", displayed);

        Assert.IsTrue(RankingEventLogReader.TryReadLine(json, out var parsed));
        Assert.AreEqual(1, parsed.ShadowV3Rank);
        Assert.AreEqual(3, parsed.ShadowV3RankDelta);

        var text = RankingExplanationFormatter.Format(
            new RankingExplanation(session, page, default, displayed),
            "pixpin",
            "PixPin",
            ranked.Path);
        StringAssert.Contains(text, "rank.shadowV3=1");
        StringAssert.Contains(text, "rank.shadowV3Delta=3");
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
