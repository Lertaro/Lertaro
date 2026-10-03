using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App.Tests.ViewModels.Settings;

// Which drives a rules change has to re-walk. Tested through the pure decision rather than through the
// live enumeration: the production entry point probes each drive's volume ID and filesystem type off the
// drive letter, so driving it directly would only ever exercise the drives the test machine happens to
// have. The probes themselves are VolumeHelper's, already covered by their own tests.
[TestClass]
public sealed class ExclusionRebuildQueueTests
{
    [TestMethod]
    public void ShouldRebuildForExclusionChange_EnabledNonJournalDrive_IsIncluded() =>
        Assert.IsTrue(ExclusionRebuildQueue.ShouldRebuildForExclusionChange(isEnabled: true, isJournalCapable: false));

    // A journal-capable drive is read whole (its build never consults ExcludedPaths), so it is unchanged
    // by a rules edit that only affects the walk.
    [TestMethod]
    public void ShouldRebuildForExclusionChange_EnabledJournalCapableDrive_IsExcluded() =>
        Assert.IsFalse(ExclusionRebuildQueue.ShouldRebuildForExclusionChange(isEnabled: true, isJournalCapable: true));

    // Not enabled means not indexed at all: there is no index for the new rules to correct.
    [TestMethod]
    public void ShouldRebuildForExclusionChange_DisabledNonJournalDrive_IsExcluded() =>
        Assert.IsFalse(ExclusionRebuildQueue.ShouldRebuildForExclusionChange(isEnabled: false, isJournalCapable: false));

    [TestMethod]
    public void ShouldRebuildForExclusionChange_DisabledJournalCapableDrive_IsExcluded() =>
        Assert.IsFalse(ExclusionRebuildQueue.ShouldRebuildForExclusionChange(isEnabled: false, isJournalCapable: true));
}
