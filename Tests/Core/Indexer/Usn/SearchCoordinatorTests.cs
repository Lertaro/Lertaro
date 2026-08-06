using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Persistence;

namespace Lertaro.Core.Tests.Indexer.Usn;

// One drive being unavailable must never stop the others being searched. Reported as "files can't be
// found while a local drive is indexing, even on a drive that isn't the one being indexed" -- which came
// from a single GLOBAL "is the index ready" check in SearchEngine, since removed. This pins the layer
// that decides what actually gets searched: every LiveIndex currently loaded, and nothing else.
[TestClass]
public sealed class SearchCoordinatorTests
{
    [TestMethod]
    public void EveryLoadedDrive_IsSearched()
    {
        using var dir = new TempDirectory();
        using var c = Load(dir, "C", "report.txt");
        using var d = Load(dir, "D", "report.md");
        var drives = new Dictionary<string, LiveIndex> { ["C"] = c, ["D"] = d };

        var hits = Search(drives, "report");

        Assert.HasCount(2, hits);
        CollectionAssert.AreEquivalent(new[] { "report.txt", "report.md" }, hits.Select(h => h.Name).ToList());
    }

    [TestMethod]
    public void ADriveMissingItsIndex_DoesNotSuppressTheOthers()
    {
        // What a rebuild actually looks like from here: for the brief window inside OnDriveCompleted the
        // rebuilt drive has no LiveIndex, and every other drive still has its own. The others must still
        // answer -- that is the whole of the reported bug.
        using var dir = new TempDirectory();
        using var d = Load(dir, "D", "report.md");
        var drives = new Dictionary<string, LiveIndex> { ["D"] = d };

        var hits = Search(drives, "report");

        Assert.HasCount(1, hits);
        Assert.AreEqual("report.md", hits[0].Name);
    }

    [TestMethod]
    public void NoDrivesLoadedAtAll_YieldsNothingRatherThanThrowing()
    {
        // A from-scratch first build really does have nothing to offer, and that has to be a quiet empty
        // result rather than an error.
        var hits = Search(new Dictionary<string, LiveIndex>(), "report");

        Assert.IsEmpty(hits);
    }

    [TestMethod]
    public void ASingleLoadedDrive_IsStillSearched()
    {
        // The coordinator has a separate one-drive path that skips the parallel fan-out.
        using var dir = new TempDirectory();
        using var c = Load(dir, "C", "notes.txt");
        var drives = new Dictionary<string, LiveIndex> { ["C"] = c };

        Assert.HasCount(1, Search(drives, "notes"));
    }

    private static List<SearchResult> Search(Dictionary<string, LiveIndex> drives, string query)
    {
        var hits = new List<SearchResult>();
        var gate = new object();
        SearchCoordinator.SearchStreaming(drives, new object(), query, 100,
            r => { lock (gate) hits.Add(r); }, CancellationToken.None, null);
        return hits;
    }

    private static LiveIndex Load(TempDirectory dir, string drive, string fileName)
    {
        var path = Path.Combine(dir.Path, $"{drive}.idx");
        SnapshotWriter.Write(BuildStore(drive, fileName), path);
        return new LiveIndex(Snapshot.Open(path));
    }

    private static FileRecordStore BuildStore(string drive, string fileName)
    {
        var store = new FileRecordStore
        {
            SourceKey = drive,
            SourceKind = FileRecordSourceKind.LocalMft,
            IdKind = FileRecordIdKind.MftFrn,
            RootId = 1,
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        store.Records.Add(new FileRecord(2, 1, fileName, FileRecordFlags.None));
        return store;
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
