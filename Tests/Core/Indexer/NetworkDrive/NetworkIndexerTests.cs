using Lertaro.Core.Indexer.NetworkDrive;

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
}
