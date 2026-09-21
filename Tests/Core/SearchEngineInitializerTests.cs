namespace Lertaro.Core.Tests;

[TestClass]
public sealed class SearchEngineInitializerTests
{
    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(false, false, false)]
    public void CanUseCachedUsnCatchUp_RequiresCompleteJournalBackedCache(
        bool isComplete,
        bool supportsUsnJournal,
        bool expected) =>
        Assert.AreEqual(expected, SearchEngineInitializer.CanUseCachedUsnCatchUp(isComplete, supportsUsnJournal));

    // An in-flight state left behind by a failed initialization is a progress bar that never moves, so it
    // must settle on the failure value; a state that already describes a usable index must survive a fault
    // raised after it was reached (monitor startup, cache save, ...).
    [TestMethod]
    [DataRow("pending")]
    [DataRow("indexing")]
    [DataRow("loading-cache")]
    [DataRow("")]
    public void ResolveStateAfterFailure_SettlesInFlightStatesOnError(string stateAtFailure) =>
        Assert.AreEqual("error", SearchEngineInitializer.ResolveStateAfterFailure(stateAtFailure));

    [TestMethod]
    [DataRow("ready")]
    [DataRow("cached")]
    [DataRow("idle")]
    public void ResolveStateAfterFailure_KeepsAlreadyUsableState(string stateAtFailure) =>
        Assert.AreEqual(stateAtFailure, SearchEngineInitializer.ResolveStateAfterFailure(stateAtFailure));
}
