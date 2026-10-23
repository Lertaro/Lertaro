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
        Assert.AreEqual(MatchQueryVariant.ProviderAlias, shadow.Evidence.QueryVariant);
    }

    [DataTestMethod]
    [DataRow(@"X:\Apps\PixPin.lnk", "PixPin.lnk", "PixPin")]
    [DataRow(@"X:\Apps\Portal.url", "Portal.url", "Portal")]
    [DataRow(@"X:\Apps\ClickOnce.appref-ms", "ClickOnce.appref-ms", "ClickOnce")]
    [DataRow(@"X:\Apps\PixPin.exe", "PixPin.exe", "PixPin.exe")]
    [DataRow(@"X:\Apps\PixPin.txt", "PixPin.txt", "PixPin.txt")]
    public void CandidateIdentity_SearchTitle_RemovesOnlyShortcutContainerExtensions(
        string path,
        string title,
        string expected)
    {
        var identity = CandidateIdentity.Create(
            RankingCandidateSource.FileIndex,
            isDirectory: false,
            isApplication: false,
            title,
            path);
        Assert.AreEqual(expected, identity.SearchTitle);
    }

    [TestMethod]
    public void CandidateIdentity_DirectoryWithShortcutSuffixIsNotAShortcut()
    {
        var identity = CandidateIdentity.Create(
            RankingCandidateSource.FileIndex,
            isDirectory: true,
            isApplication: false,
            "Archive.lnk",
            @"X:\Apps\Archive.lnk");

        Assert.AreEqual(RankingCandidateKind.Directory, identity.Kind);
        Assert.AreEqual("Archive.lnk", identity.SearchTitle);
    }

    [TestMethod]
    public void CandidateIdentity_ExeRemainsAFile()
    {
        var identity = CandidateIdentity.Create(
            RankingCandidateSource.FileIndex,
            isDirectory: false,
            isApplication: false,
            "PixPin.exe",
            @"X:\Apps\PixPin.exe");

        Assert.AreEqual(RankingCandidateKind.File, identity.Kind);
        Assert.AreEqual("PixPin.exe", identity.SearchTitle);
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
