using Lertaro.Core.Services.HookLaunch;

namespace Lertaro.Core.Tests.Services.HookLaunch;

[TestClass]
public sealed class RejectionLogThrottleTests
{
    [TestMethod]
    public void ShouldLog_FirstRejection_ReturnsTrue()
    {
        var throttle = new RejectionLogThrottle(relogIntervalMs: 600_000);

        Assert.IsTrue(throttle.ShouldLog(pid: 1000, nowTickCount: 0));
    }

    [TestMethod]
    public void ShouldLog_SamePidWithinInterval_SuppressesRepeat()
    {
        var throttle = new RejectionLogThrottle(relogIntervalMs: 600_000);

        Assert.IsTrue(throttle.ShouldLog(pid: 1000, nowTickCount: 0));
        Assert.IsFalse(throttle.ShouldLog(pid: 1000, nowTickCount: 599_999));
        Assert.IsFalse(throttle.ShouldLog(pid: 1000, nowTickCount: 60_000));
    }

    [TestMethod]
    public void ShouldLog_SamePidAfterInterval_Rewarns()
    {
        var throttle = new RejectionLogThrottle(relogIntervalMs: 600_000);

        Assert.IsTrue(throttle.ShouldLog(pid: 1000, nowTickCount: 0));
        Assert.IsTrue(throttle.ShouldLog(pid: 1000, nowTickCount: 600_000),
            "a PID reused by a different process must become visible again");
    }

    [TestMethod]
    public void ShouldLog_DifferentPid_LogsIndependently()
    {
        var throttle = new RejectionLogThrottle(relogIntervalMs: 600_000);

        Assert.IsTrue(throttle.ShouldLog(pid: 1000, nowTickCount: 0));
        Assert.IsTrue(throttle.ShouldLog(pid: 2000, nowTickCount: 0));
    }
}
