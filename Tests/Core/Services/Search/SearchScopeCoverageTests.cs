using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

// Covers the pure routing decision behind SearchScopeCoverage -- the same three-way rule the real
// search routing applies (WSL always in-process; UNC/network needs a containing configured root; a
// local drive must be enabled for indexing). The I/O shell around it (drive probing, root listing,
// caching, warning) is deliberately not exercised here.
[TestClass]
public sealed class SearchScopeCoverageTests
{
    [TestMethod]
    public void WslPath_IsCovered_RegardlessOfEverythingElse()
    {
        Assert.IsTrue(SearchScopeCoverage.DecideCovered(isWsl: true, isNetworkSource: true, hasInProcessRoot: false, isLocalDriveEnabled: false));
        Assert.IsTrue(SearchScopeCoverage.DecideCovered(isWsl: true, isNetworkSource: false, hasInProcessRoot: false, isLocalDriveEnabled: false));
    }

    [TestMethod]
    public void NetworkSource_NeedsAContainingInProcessRoot()
    {
        Assert.IsTrue(SearchScopeCoverage.DecideCovered(isWsl: false, isNetworkSource: true, hasInProcessRoot: true, isLocalDriveEnabled: true));
        Assert.IsFalse(SearchScopeCoverage.DecideCovered(isWsl: false, isNetworkSource: true, hasInProcessRoot: false, isLocalDriveEnabled: true), "a configured local drive must not vouch for a network path");
    }

    [TestMethod]
    public void LocalPath_IsCoveredByAnInProcessFolderRoot() => Assert.IsTrue(SearchScopeCoverage.DecideCovered(isWsl: false, isNetworkSource: false, hasInProcessRoot: true, isLocalDriveEnabled: false));

    [TestMethod]
    public void LocalPath_WithoutFolderRoot_NeedsAnEnabledDrive()
    {
        Assert.IsTrue(SearchScopeCoverage.DecideCovered(isWsl: false, isNetworkSource: false, hasInProcessRoot: false, isLocalDriveEnabled: true));
        Assert.IsFalse(SearchScopeCoverage.DecideCovered(isWsl: false, isNetworkSource: false, hasInProcessRoot: false, isLocalDriveEnabled: false));
    }
}
