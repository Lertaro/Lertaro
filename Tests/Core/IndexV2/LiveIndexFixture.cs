using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Persistence;

namespace Lertaro.Core.Tests.IndexV2;

// Builds a real (non-mocked) LiveIndex for tests that need actual Snapshot+DeltaOverlay search/merge
// behavior: writes an in-memory FileRecordStore through the real SnapshotWriter into a temp file, then
// opens it via Snapshot.Open the same way production code does. No production code is touched or
// modified -- this only calls existing public API (SnapshotWriter.Write / Snapshot.Open / LiveIndex).
internal sealed class LiveIndexFixture : IDisposable
{
    private readonly string _tempDir;

    public string Path { get; }
    public LiveIndex Index { get; }

    private LiveIndexFixture(string tempDir, string path, LiveIndex index)
    {
        _tempDir = tempDir;
        Path = path;
        Index = index;
    }

    // driveKey mirrors FileRecordStore.SourceKey (e.g. "C" for a local drive) -- becomes Snapshot.SourceKey
    // and, via PathHelpers.BuildSourceRoot, Snapshot.SourceRoot (e.g. "C:\").
    public static LiveIndexFixture Build(string driveKey, IEnumerable<FileRecord> records, bool isComplete = false)
    {
        var tempDir = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        var path = System.IO.Path.Combine(tempDir, "test.idx");

        var store = new FileRecordStore
        {
            SourceKey = driveKey,
            SourceKind = FileRecordSourceKind.LocalMft,
            IdKind = FileRecordIdKind.MftFrn,
            RootId = 1,
            IsComplete = isComplete,
        };
        store.Records.AddRange(records);

        SnapshotWriter.Write(store, path);
        var snapshot = Snapshot.Open(path);
        return new LiveIndexFixture(tempDir, path, new LiveIndex(snapshot));
    }

    // The conventional self-parented root row every fixture needs at id 1 (see NetworkIndex.Build /
    // LocalDriveWalkBuilder.Build / IndexCacheManager.CreateEmptyStore for the same convention elsewhere).
    public static FileRecord Root() => new(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot);

    public void Dispose()
    {
        Index.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
