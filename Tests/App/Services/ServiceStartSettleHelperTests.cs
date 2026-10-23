using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

// The wait is the difference between "the service is slow" and "the service is broken": reinstalling while a
// start is in flight stops the service and re-ACLs its own tree underneath itself, which is how one reported
// boot ended with no service registered at all. These probe the decision with a fake, never a live service.
[TestClass]
public sealed class ServiceStartSettleHelperTests
{
    [TestMethod]
    public void Wait_NotPending_ReturnsImmediately()
    {
        var probes = 0;

        var settled = ServiceStartSettleHelper.Wait(() => { probes++; return false; }, timeoutMs: 1000, pollMs: 1);

        Assert.IsTrue(settled);
        Assert.AreEqual(1, probes);
    }

    [TestMethod]
    public void Wait_SettlesDuringTheWait_ReturnsTrue()
    {
        var probes = 0;

        var settled = ServiceStartSettleHelper.Wait(() => ++probes < 3, timeoutMs: 1000, pollMs: 1);

        Assert.IsTrue(settled);
        Assert.AreEqual(3, probes);
    }

    // The bound is what keeps a hung service from hanging the App's recovery forever: past it the start has
    // failed, and the caller installs again.
    [TestMethod]
    public void Wait_StillPendingPastTheBudget_GivesUpAndReportsFalse()
    {
        var settled = ServiceStartSettleHelper.Wait(() => true, timeoutMs: 20, pollMs: 1);

        Assert.IsFalse(settled);
    }

    [TestMethod]
    public void Wait_AlreadyExpiredBudget_ProbesOnceAndStops()
    {
        var probes = 0;

        var settled = ServiceStartSettleHelper.Wait(() => { probes++; return true; }, timeoutMs: 0, pollMs: 1);

        Assert.IsFalse(settled);
        Assert.AreEqual(1, probes);
    }
}
