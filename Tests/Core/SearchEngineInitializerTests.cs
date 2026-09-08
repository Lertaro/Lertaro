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
}
