using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

[TestClass]
public sealed class SearchRankingEvidenceTests
{
    [TestMethod]
    public void Accepted_DirectIdentityGetsExactGradeWithoutInventingModeOrSpan()
    {
        var evidence = MatchEvidence.Accepted(
            "pixpin",
            "PixPin.lnk",
            MatchEvidenceField.FileName,
            new MatchRank(MatchRank.TierName, 0, 1));

        Assert.AreEqual(MatchEvidenceField.FileName, evidence.Field);
        Assert.AreEqual(MatchEvidenceGrade.ExactIdentity, evidence.Grade);
        Assert.AreEqual(MatchEvidenceMode.LegacyUnknown, evidence.Mode);
        Assert.AreEqual(-1, evidence.SpanStart);
        Assert.IsTrue(evidence.AllPositiveTerms);
        Assert.IsTrue(evidence.DirectText);
    }

    [TestMethod]
    public void Accepted_SecondaryAndLearnedFieldsKeepTheirProvenance()
    {
        var rank = new MatchRank(MatchRank.TierName, 0, 1);
        var alias = MatchEvidence.Accepted("tg", "Telegram", MatchEvidenceField.CuratedAlias, rank);
        var learned = MatchEvidence.Accepted("notes", "notes", MatchEvidenceField.HistoryKeyword, rank,
            MatchEvidenceMode.LearnedAlias);

        Assert.AreEqual(MatchEvidenceGrade.SecondaryField, alias.Grade);
        Assert.IsFalse(alias.DirectText);
        Assert.AreEqual(MatchEvidenceGrade.LearnedAlias, learned.Grade);
        Assert.AreEqual(MatchEvidenceMode.LearnedAlias, learned.Mode);
    }

    [TestMethod]
    public void EvidenceAdapter_ReturnsTheSameRankAsTheExistingBestMatchPath()
    {
        var query = FuzzyQuery.Parse("capture");
        var aliases = new[] { "screen capture", "screenshot" };

        var existing = query.BestMatch("PixPin", aliases);
        var shadow = query.BestMatchWithEvidence(
            "PixPin",
            MatchEvidenceField.DisplayTitle,
            aliases.Select(alias => (alias, MatchEvidenceField.ProviderAlias)));

        Assert.AreEqual(existing, shadow.Rank);
        Assert.AreEqual(MatchEvidenceField.ProviderAlias, shadow.Evidence.Field);
    }

    [TestMethod]
    public void CandidateIdentity_SeparatesSourceAndExecutionKinds()
    {
        var identity = CandidateIdentity.Create(
            RankingCandidateSource.FileIndex | RankingCandidateSource.SearchableProvider,
            isDirectory: false,
            isApplication: true,
            "PixPin",
            @"X:\Apps\PixPin.lnk");

        Assert.AreEqual(
            RankingCandidateKind.File | RankingCandidateKind.Application | RankingCandidateKind.Shortcut,
            identity.Kind);
        Assert.AreEqual("PixPin", identity.SearchTitle);
        Assert.AreEqual(@"X:\Apps\PixPin.lnk", identity.PathKey);
    }
}
