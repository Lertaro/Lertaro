using System.Text.RegularExpressions;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

[TestClass]
[DoNotParallelize]
public sealed class RankingEventLogStorageTests
{
    private string _dir = null!;
    private string MainPath => Path.Combine(_dir, "ranking-events.jsonl");

    [TestInitialize]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LertaroRankingStorage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        RankingEventLog.DirectoryOverride = _dir;
        RankingEventLog.EnabledOverride = true;
    }

    [TestCleanup]
    public void TearDown()
    {
        RankingEventLog.DirectoryOverride = null;
        RankingEventLog.EnabledOverride = null;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static RankingLogRow Row(string path, int kernelRank) =>
        new(path, kernelRank, RankSection.Boosted, 18, 0, IndexRuleEffect.Normal,
            HistoryTier.None, 0, MatchRank.NoMatch, false, false, false, false,
            RankingCandidateSource.FileIndex);

    private static RankingLogRow[] Rows(int count) =>
        Enumerable.Range(1, count).Select(i => Row($@"D:\page\file{i}.txt", i)).ToArray();

    [TestMethod]
    public void RotatesAboveTenMegabytes()
    {
        using (var stream = File.Create(MainPath))
            stream.SetLength(10 * 1024 * 1024 + 1);
        RankingEventLog.RecordQuery("atlas", [Row(@"D:\a.txt", 1)]);
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "ranking-events.1.jsonl")));
        Assert.AreEqual(2, File.ReadAllLines(MainPath).Length);
    }

    [TestMethod]
    public void PrunesFilesOlderThanThirtyDays()
    {
        var stale = Path.Combine(_dir, "ranking-events.2.jsonl");
        File.WriteAllText(stale, "{}");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-40));
        RankingEventLog.RecordQuery("atlas", [Row(@"D:\a.txt", 1)]);
        Assert.IsFalse(File.Exists(stale));
    }

    [TestMethod]
    public void HashKey_IsStableCaseInsensitiveAndTwelveHexChars()
    {
        Assert.AreEqual(RankingEventLog.HashKey("ABC"), RankingEventLog.HashKey(" abc "));
        Assert.AreEqual(12, RankingEventLog.HashKey("abc").Length);
        Assert.IsTrue(Regex.IsMatch(RankingEventLog.HashKey("abc"), "^[0-9a-f]{12}$"));
    }

    [TestMethod]
    public void RecordPageAndOpen_NeverThrowWhenDirectoryIsUnusable()
    {
        var blocked = Path.Combine(_dir, "not-a-directory");
        File.WriteAllText(blocked, "x");
        RankingEventLog.DirectoryOverride = blocked;

        RankingEventLog.RecordQuery("atlas", Rows(2));
        RankingEventLog.RecordOpened("atlas", @"D:\page\file1.txt");
    }
}
