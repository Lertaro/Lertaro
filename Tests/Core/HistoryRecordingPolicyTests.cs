namespace Lertaro.Core.Tests;

[TestClass]
public sealed class HistoryRecordingPolicyTests
{
    [TestMethod]
    public void DisabledStore_DoesNotCreateNewEntry()
        => Assert.IsFalse(HistoryRecordingPolicy.ShouldRecord(enabled: false, entryExists: false));

    [TestMethod]
    public void DisabledStore_UpdatesExistingEntry()
        => Assert.IsTrue(HistoryRecordingPolicy.ShouldRecord(enabled: false, entryExists: true));

    [TestMethod]
    public void EnabledStore_CreatesNewEntry()
        => Assert.IsTrue(HistoryRecordingPolicy.ShouldRecord(enabled: true, entryExists: false));
}
