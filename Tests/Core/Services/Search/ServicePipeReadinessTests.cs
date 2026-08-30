using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

[TestClass]
public sealed class ServicePipeReadinessTests
{
    [TestMethod]
    public void IsColdStart_NeverConnectedInsideWindow_True()
    {
        var readiness = new ServicePipeReadiness(coldStartWindowMs: 1000, startedAtTickCount: 0);

        Assert.IsTrue(readiness.IsColdStart(nowTickCount: 500));
    }

    [TestMethod]
    public void IsColdStart_AfterFirstConnection_FalseEvenInsideWindow()
    {
        var readiness = new ServicePipeReadiness(coldStartWindowMs: 1000, startedAtTickCount: 0);
        readiness.MarkConnected();

        Assert.IsFalse(readiness.IsColdStart(nowTickCount: 500),
            "once the pipe has answered, failures are real faults again");
    }

    [TestMethod]
    public void IsColdStart_NeverConnectedPastWindow_False()
    {
        var readiness = new ServicePipeReadiness(coldStartWindowMs: 1000, startedAtTickCount: 0);

        Assert.IsFalse(readiness.IsColdStart(nowTickCount: 1000),
            "past the window an unreachable service is a real fault again (service down or not installed)");
        Assert.IsFalse(readiness.IsColdStart(nowTickCount: 60_000));
    }
}
