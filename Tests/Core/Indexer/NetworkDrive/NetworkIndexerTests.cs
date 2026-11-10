using System.Reflection;

using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.Core.SearchIndex.Query;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive;

[TestClass]
public sealed class NetworkIndexerTests
{
    [TestMethod]
    public void RefreshDrive_PendingProbe_PreservesCachedItemCount()
    {
        using var indexer = new NetworkIndexer();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(NetworkIndexer).GetField("_configured", flags)!.SetValue(indexer, true);
        var modes = (Dictionary<string, string>)typeof(NetworkIndexer).GetField("_refreshModes", flags)!.GetValue(indexer)!;
        var statuses = (Dictionary<string, NetworkIndexStatus>)typeof(NetworkIndexer).GetField("_statuses", flags)!.GetValue(indexer)!;
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        modes[root] = "Manual";
        statuses[root] = new NetworkIndexStatus { Drive = root, State = "cached", Items = 42 };

        Assert.IsTrue(indexer.RefreshDrive(root));

        var status = Assert.ContainsSingle(indexer.GetStatuses());
        Assert.AreEqual("indexing", status.State);
        Assert.AreEqual(42, status.Items);
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(int.MaxValue)]
    public void SearchSnapshots_RegexTimeoutFailsTheWholeQuery(int limit)
    {
        using var first = NetworkIndex.FromStore(BuildShareStore("Y", new string('a', 30) + "!"));
        using var second = NetworkIndex.FromStore(BuildShareStore("Z", "a"));
        const string query = @"/^(?=(a+)+$)/";
        var error = Assert.Throws<Exception>(() => NetworkIndexerSearchExtensions.SearchSnapshots(
            [first, second], SearchQueryParser.Parse(query), query, null, limit, _ => { }, CancellationToken.None));
        Assert.IsTrue(Core.SearchIndex.SearchRegexTimeoutException.IsTimeout(error));
    }
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(int.MaxValue)]
    public void SearchSnapshots_CategoryReachesEachShareBeforeItsLimit(int limit)
    {
        using var first = NetworkIndex.FromStore(BuildShareStore("Y", "report.exe", "report-a.png"));
        using var second = NetworkIndex.FromStore(BuildShareStore("Z", "report.exe", "report-b.png"));
        var rows = new List<SearchResult>();
        NetworkIndexerSearchExtensions.SearchSnapshots([first, second], SearchQueryParser.Parse("report"), "report", null,
            limit, rows.Add, CancellationToken.None, category: PluginSdk.Helpers.SearchCategoryFilter.Image);
        Assert.HasCount(Math.Min(2, limit), rows);
        Assert.IsTrue(rows.All(r => r.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)));
    }

    // Regression coverage: "complete" on its own must not count as cached when the pass finished with
    // directories it never captured. Gating on IsComplete alone is what left those directories missing
    // forever under the default Manual refresh mode, because nothing ever ran a second pass.
    [TestMethod]
    public void IsFullyCached_CompleteIndexWithUncapturedDirectories_IsNotFullyCached()
    {
        Assert.IsTrue(NetworkIndexer.IsFullyCached(isComplete: true, hasUncapturedDirectories: false));
        Assert.IsFalse(NetworkIndexer.IsFullyCached(isComplete: true, hasUncapturedDirectories: true));
        Assert.IsFalse(NetworkIndexer.IsFullyCached(isComplete: false, hasUncapturedDirectories: false));
        Assert.IsFalse(NetworkIndexer.IsFullyCached(isComplete: false, hasUncapturedDirectories: true));
    }

    [TestMethod]
    public void SearchSnapshots_BoundedLimitKeepsBestMatchesAcrossShares_NotFirstToArrive()
    {
        using var noisyShare = NetworkIndex.FromStore(BuildShareStore(
            "Y",
            Enumerable.Range(0, 12).Select(i => $"AutofillStates{i}").ToArray()));
        using var exactShare = NetworkIndex.FromStore(BuildShareStore("Z", "code-atlas"));

        var hits = new List<SearchResult>();
        NetworkIndexerSearchExtensions.SearchSnapshots(
            [noisyShare, exactShare],
            SearchQueryParser.Parse("atlas"),
            "atlas",
            directoryFilterLower: null,
            limit: 3,
            hits.Add,
            CancellationToken.None);

        Assert.HasCount(3, hits);
        Assert.AreEqual("code-atlas", hits[0].Name);
    }

    private static FileRecordStore BuildShareStore(string drive, params string[] names)
    {
        var store = new FileRecordStore
        {
            SourceKey = drive,
            SourceKind = FileRecordSourceKind.NetworkMappedDrive,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = 1,
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        for (var i = 0; i < names.Length; i++)
            store.Records.Add(new FileRecord((ulong)(i + 2), 1, names[i], FileRecordFlags.None));
        return store;
    }
}
