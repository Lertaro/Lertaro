using System.Diagnostics;
using Lertaro.App.Services.Plugin;

namespace Lertaro.App.Tests.Services.Plugin;

[TestClass]
public sealed class PluginPerformanceMonitorTests
{
    [TestMethod]
    public void CounterAccumulatesCallsAndKeepsLargestDuration()
    {
        var counter = new PluginPerformanceMonitor.PluginPerformanceCounter();

        counter.Record(Stopwatch.Frequency / 10, 1024, false);
        counter.Record(Stopwatch.Frequency / 2, 2048, false);

        var snapshot = counter.Snapshot();
        Assert.AreEqual(2L, snapshot.InvocationCount);
        Assert.AreEqual(Stopwatch.Frequency / 10 + Stopwatch.Frequency / 2, snapshot.TotalElapsedTicks);
        Assert.AreEqual(Stopwatch.Frequency / 2, snapshot.MaxElapsedTicks);
        Assert.AreEqual(3L * 1024, snapshot.TotalAllocatedBytes);
        Assert.AreEqual(0L, snapshot.ExceptionCount);
    }

    [TestMethod]
    public void CounterIncludesFailedCallsAndIgnoresNegativeAllocationDelta()
    {
        var counter = new PluginPerformanceMonitor.PluginPerformanceCounter();

        counter.Record(20, -1, true);

        var snapshot = counter.Snapshot();
        Assert.AreEqual(1L, snapshot.InvocationCount);
        Assert.AreEqual(20L, snapshot.LastElapsedTicks);
        Assert.AreEqual(0L, snapshot.TotalAllocatedBytes);
        Assert.AreEqual(1L, snapshot.ExceptionCount);
    }
}
