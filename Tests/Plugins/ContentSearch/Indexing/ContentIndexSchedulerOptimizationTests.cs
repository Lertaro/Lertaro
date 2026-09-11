using Lertaro.Plugins.ContentSearch.Indexing;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

[TestClass]
public sealed class ContentIndexSchedulerOptimizationTests
{
    [TestMethod]
    public void ShouldRunIdleMaintenance_RequiresQueuedWorkAndThreeSecondsOfIdle()
    {
        Assert.IsFalse(ContentIndexScheduler.ShouldRunIdleMaintenance(false, 15));
        Assert.IsFalse(ContentIndexScheduler.ShouldRunIdleMaintenance(true, 14));
        Assert.IsTrue(ContentIndexScheduler.ShouldRunIdleMaintenance(true, 15));
    }
}
