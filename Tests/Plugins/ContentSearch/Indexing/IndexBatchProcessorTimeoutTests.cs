using Lertaro.Plugins.ContentSearch.Indexing;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

[TestClass]
public sealed class IndexBatchProcessorTimeoutTests
{
    [TestMethod]
    public void IsFileTimeout_OnlyTreatsIndependentTimeoutAsFileFailure()
    {
        using var fileTimeout = new CancellationTokenSource();
        using var batch = new CancellationTokenSource();

        fileTimeout.Cancel();
        Assert.IsTrue(IndexBatchProcessor.IsFileTimeout(fileTimeout.Token, batch.Token));

        batch.Cancel();
        Assert.IsFalse(IndexBatchProcessor.IsFileTimeout(fileTimeout.Token, batch.Token));
    }
}
