using System.Text.Json;
using System.Text.RegularExpressions;

using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

// The log keeps process-wide state (the collapse signature, the once-per-process age prune) and its test
// seams are static, so these cases must not run against each other or against anything else that writes.
[TestClass]
[DoNotParallelize]
public sealed class RankingEventLogTests
{
    private string _dir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LertaroRankingLog_" + Guid.NewGuid().ToString("N"));
        // The log creates its directory on the first write; the cases that pre-stage a file need it now.
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

    private static RankingLogRow Row(string path, int rank) =>
        new(
            path,
            rank,
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
            false);

    private static RankingLogRow[] Page(int count) =>
        Enumerable.Range(1, count).Select(i => Row($@"D:\page\file{i}.txt", i)).ToArray();

    // A page line plus one line per row: the page is what carries the query's shared context (type order,
    // candidate count), so every assertion below skips it rather than counting it as a row.
    private JsonElement[] RowLines() =>
        File.ReadAllLines(MainPath)
            .Skip(1)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .ToArray();

    [TestMethod]
    public void RecordQuery_WritesHashesInsteadOfText()
    {
        RankingEventLog.RecordQuery("secret-query", [Row(@"C:\secret\place\file.txt", 1)]);

        var text = Read();
        Assert.IsTrue(text.Contains("\"rank\":1"), text);
        Assert.IsTrue(text.Contains("\"bucket\""), text);
        Assert.IsTrue(text.Contains("\"score\""), text);
        Assert.IsTrue(text.Contains("\"opened\":false"), text);
        Assert.IsTrue(text.Contains($"\"policyVersion\":{RankPolicy.Version}"), text);
        Assert.IsFalse(text.Contains("secret-query"), text);
        Assert.IsFalse(text.Contains("file.txt"), text);

        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
        using var document = JsonDocument.Parse(line);
        var queryHash = document.RootElement.GetProperty("queryHash").GetString()!;
        Assert.IsTrue(Regex.IsMatch(queryHash, "^[0-9a-f]{12}$"), queryHash);
    }

    [TestMethod]
    public void RecordQuery_CapsAtTenRows()
    {
        RankingEventLog.RecordQuery("atlas", Page(12));

        var ranks = RowLines()
            .Select(element => element.GetProperty("rank").GetInt32())
            .ToArray();
        Assert.AreEqual(10, ranks.Length);
        CollectionAssert.AreEqual(Enumerable.Range(1, 10).ToArray(), ranks);
    }

    [TestMethod]
    public void RecordQuery_WritesPageLineWithTheTypeOrder()
    {
        RankingEventLog.RecordQuery("atlas", Page(2), ["__builtin::Files", "CoreExtensions.StartMenu"]);

        var lines = File.ReadAllLines(MainPath);
        Assert.AreEqual(3, lines.Length);
        var page = JsonDocument.Parse(lines[0]).RootElement;
        Assert.AreEqual("page", page.GetProperty("kind").GetString());
        Assert.AreEqual(2, page.GetProperty("rows").GetInt32());
        Assert.AreEqual(2, page.GetProperty("shown").GetInt32());
        Assert.AreEqual("__builtin::Files", page.GetProperty("order")[0].GetString());
        Assert.AreEqual("CoreExtensions.StartMenu", page.GetProperty("order")[1].GetString());
        Assert.AreEqual(RankingEventLog.SchemaVersion, page.GetProperty("v").GetInt32());
    }

    [TestMethod]
    public void RecordQuery_AnEmptyTypeOrderIsAnEmptyArray()
    {
        RankingEventLog.RecordQuery("atlas", Page(1));

        var page = JsonDocument.Parse(File.ReadAllLines(MainPath)[0]).RootElement;
        Assert.AreEqual(0, page.GetProperty("order").GetArrayLength());
    }

    [TestMethod]
    public void RecordQuery_DecomposesTheKeyWithoutNamingAnything()
    {
        // A .lnk four levels down with a strong whole-word match in the first result type: exactly the row
        // the calibration log has to make arguable ("why is this above that folder?") without the text.
        var path = @"D:\tools\smalltools\Auslogics Duplicate File Finder.lnk";
        var row = new RankingLogRow(
            path,
            1,
            RankSection.Boosted,
            34,
            0,
            IndexRuleEffect.Normal,
            HistoryTier.H5,
            5000,
            new MatchRank(MatchRank.TierName, 0, 0.742, 1),
            false,
            false,
            false,
            false);
        RankingEventLog.RecordQuery("auslogics", [row]);

        var element = RowLines()[0];
        Assert.AreEqual(34, element.GetProperty("score").GetInt32());
        Assert.AreEqual(0, element.GetProperty("typeRank").GetInt32());
        Assert.AreEqual(1, element.GetProperty("prio").GetInt32());
        Assert.AreEqual((int)HistoryTier.H5, element.GetProperty("hTier").GetInt32());
        Assert.AreEqual(5000, element.GetProperty("histTie").GetInt32());
        Assert.AreEqual(1, element.GetProperty("strong").GetInt32());
        Assert.AreEqual(742, element.GetProperty("mCov").GetInt32());
        Assert.AreEqual(1000, element.GetProperty("mCont").GetInt32());
        Assert.AreEqual(1, element.GetProperty("ext").GetInt32());
        Assert.AreEqual(4, element.GetProperty("depth").GetInt32());
        Assert.AreEqual(Path.GetFileName(path).Length, element.GetProperty("nameLen").GetInt32());

        var text = Read();
        Assert.IsFalse(text.Contains("Auslogics"), text);
        Assert.IsFalse(text.Contains("smalltools"), text);
        Assert.IsFalse(text.Contains("auslogics"), text);
    }

    [TestMethod]
    public void RecordQuery_SameTopRowsWithinFiveSeconds_WritesOnce()
    {
        var page = Page(10);
        RankingEventLog.RecordQuery("atlas", page);
        RankingEventLog.RecordQuery("atlas", page);

        // One page line plus the ten rows.
        Assert.AreEqual(11, File.ReadAllLines(MainPath).Length);
    }

    [TestMethod]
    public void RecordQuery_DifferentQuery_AppendsBoth()
    {
        var page = Page(1);
        RankingEventLog.RecordQuery("atlas", page);
        RankingEventLog.RecordQuery("atlas2", page);

        // Two pages, each one page line plus its single row.
        Assert.AreEqual(4, File.ReadAllLines(MainPath).Length);
    }

    [TestMethod]
    public void Disabled_WritesNothing()
    {
        RankingEventLog.EnabledOverride = false;

        RankingEventLog.RecordQuery("atlas", Page(3));
        RankingEventLog.RecordOpened("atlas", @"D:\page\file1.txt");

        Assert.IsFalse(File.Exists(MainPath));
    }

    [TestMethod]
    public void RecordOpened_WritesOpenedLineWithoutRank()
    {
        RankingEventLog.RecordOpened("atlas", @"D:\page\file1.txt");

        var text = Read();
        Assert.IsTrue(text.Contains("\"opened\":true"), text);
        Assert.IsFalse(text.Contains("\"rank\""), text);
        Assert.IsFalse(text.Contains("file1"), text);
    }

    [TestMethod]
    public void RotatesAboveTenMegabytes()
    {
        using (var stream = File.Create(MainPath))
            stream.SetLength(10 * 1024 * 1024 + 1);

        RankingEventLog.RecordQuery("atlas", Page(1));

        Assert.IsTrue(File.Exists(Path.Combine(_dir, "ranking-events.1.jsonl")));
        // The fresh file holds this query's page line and its single row.
        Assert.AreEqual(2, File.ReadAllLines(MainPath).Length);
    }

    [TestMethod]
    public void PrunesFilesOlderThanThirtyDays()
    {
        var stale = Path.Combine(_dir, "ranking-events.2.jsonl");
        File.WriteAllText(stale, "{}");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-40));

        RankingEventLog.RecordQuery("atlas", Page(1));

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
    public void RecordQuery_NeverThrowsWhenDirectoryIsUnusable()
    {
        // A plain file where a directory has to be: the create throws and the log swallows it, so the
        // search that only wanted to record a page still finishes.
        var blocked = Path.Combine(_dir, "not-a-directory");
        File.WriteAllText(blocked, "x");
        RankingEventLog.DirectoryOverride = blocked;

        RankingEventLog.RecordQuery("atlas", Page(2));
        RankingEventLog.RecordOpened("atlas", @"D:\page\file1.txt");
    }
}
