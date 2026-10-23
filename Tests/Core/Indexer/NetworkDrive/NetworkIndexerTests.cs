using Lertaro.Core.IndexV2;
using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.Core.SearchIndex.Query;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive;

// Only the pure cachedDrives decision is covered here: Configure itself loads UserSettings and resolves
// real cache paths under Logger.UserDataDir (NetworkDriveCacheLocator's WNet syscalls), the same
// non-injectable-real-path hazard NetworkIndexerPublisherTests already documents.
[TestClass]
public sealed class NetworkIndexerTests
{
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
