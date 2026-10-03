using Lertaro.Core.Indexer.NetworkDrive.Scheduling;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Scheduling;

// The marker file is the persisted half of "complete, but directories were never captured": it has to
// survive a restart for the next Configure to hand NetworkIndexer.IsFullyCached the truth. Only the file
// handling is covered here -- RefreshDrive itself needs a real drive to walk.
[TestClass]
public sealed class DriveRefreshRunnerTests
{
    [TestMethod]
    public void UncapturedMarker_SetThenCleared_RoundTripsNextToTheCacheFile()
    {
        using var dir = new TempDirectory();
        var cachePath = Path.Combine(dir.Path, "cache.idx");

        Assert.IsFalse(DriveRefreshRunner.HasUncapturedMarker(cachePath));

        DriveRefreshRunner.SetUncapturedMarker(cachePath, uncaptured: true);

        Assert.IsTrue(DriveRefreshRunner.HasUncapturedMarker(cachePath));
        Assert.IsTrue(File.Exists(cachePath + DriveRefreshRunner.UncapturedMarkerSuffix));
        // The marker must not look like a cache: NetworkDriveCacheLocator lists cached drives by globbing
        // "*.idx", so a marker matching that pattern would surface as a cached drive of its own.
        Assert.IsEmpty(Directory.EnumerateFiles(dir.Path, "*.idx").ToList());

        DriveRefreshRunner.SetUncapturedMarker(cachePath, uncaptured: false);

        Assert.IsFalse(DriveRefreshRunner.HasUncapturedMarker(cachePath));
    }

    // The clean-pass path clears a marker that may well not be there (the common case), which must not
    // throw -- RefreshDrive's own catch would otherwise turn a finished scan into an "error" status.
    [TestMethod]
    public void SetUncapturedMarker_ClearingAnAbsentMarker_IsANoOp()
    {
        using var dir = new TempDirectory();
        var cachePath = Path.Combine(dir.Path, "cache.idx");

        DriveRefreshRunner.SetUncapturedMarker(cachePath, uncaptured: false);

        Assert.IsFalse(DriveRefreshRunner.HasUncapturedMarker(cachePath));
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
