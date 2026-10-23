using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

// The retry gate used to be one process-wide boolean: a single failed install attempt ended every window's
// recovery for the rest of the session, which is what made "the service is still coming up after boot" show as
// a permanent Degraded Mode. These are the three pure decisions behind that gate; the state they read lives in
// the handler, so nothing here touches a live service.
[TestClass]
public sealed class ServiceConnectionMonitorTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void MayAttemptAutoInstall_FirstAttempt_IsAllowedWithoutWaiting() =>
        Assert.IsTrue(ServiceConnectionMonitor.MayAttemptAutoInstall(0, Now, DateTime.MinValue));

    [TestMethod]
    [DataRow(1, 59)]
    [DataRow(2, 119)]
    [DataRow(3, 239)]
    public void MayAttemptAutoInstall_InsideTheCooldown_IsHeldBack(int attempts, int secondsElapsed)
    {
        var retryAfter = Now.Add(ServiceConnectionMonitor.AutoInstallCooldown(attempts));

        Assert.IsFalse(ServiceConnectionMonitor.MayAttemptAutoInstall(attempts, Now.AddSeconds(secondsElapsed), retryAfter));
    }

    [TestMethod]
    [DataRow(1, 60)]
    [DataRow(2, 120)]
    public void MayAttemptAutoInstall_WhenTheCooldownRunsOut_IsAllowedAgain(int attempts, int secondsElapsed)
    {
        var retryAfter = Now.Add(ServiceConnectionMonitor.AutoInstallCooldown(attempts));

        Assert.IsTrue(ServiceConnectionMonitor.MayAttemptAutoInstall(attempts, Now.AddSeconds(secondsElapsed), retryAfter));
    }

    // The cap is what keeps an install that can never succeed from prompting for elevation forever.
    [TestMethod]
    public void MayAttemptAutoInstall_SpentBudget_IsRefusedEvenAfterTheCooldown()
    {
        Assert.IsFalse(ServiceConnectionMonitor.MayAttemptAutoInstall(
            ServiceConnectionMonitor.MaximumAutoInstallAttempts, Now.AddYears(1), DateTime.MinValue));
    }

    [TestMethod]
    public void ShouldWaitForAutoInstallRetry_BeforeTheFirstAttempt_DoesNotHoldTheAttemptBack() =>
        Assert.IsFalse(ServiceConnectionMonitor.ShouldWaitForAutoInstallRetry(0, Now, Now.AddSeconds(60)));

    [TestMethod]
    public void ShouldWaitForAutoInstallRetry_DuringTheCooldown_KeepsReportingReconnecting() =>
        Assert.IsTrue(ServiceConnectionMonitor.ShouldWaitForAutoInstallRetry(1, Now, Now.AddSeconds(60)));

    [TestMethod]
    public void ShouldWaitForAutoInstallRetry_AfterTheLastScheduledAttempt_DegradesInstead()
    {
        Assert.IsFalse(ServiceConnectionMonitor.ShouldWaitForAutoInstallRetry(
            ServiceConnectionMonitor.MaximumAutoInstallAttempts - 1, Now.AddSeconds(61), Now.AddSeconds(60)));
        Assert.IsFalse(ServiceConnectionMonitor.ShouldWaitForAutoInstallRetry(
            ServiceConnectionMonitor.MaximumAutoInstallAttempts, Now, Now.AddSeconds(60)));
    }

    // The two predicates have to partition the waiting period: any moment is either "try now" or "keep waiting",
    // never both (a retry during a cooldown) and never neither (silent Degraded Mode before the budget is spent).
    [TestMethod]
    public void AttemptGate_NeverBothAllowsAndWaitsAndNeverStopsMidBackoff()
    {
        for (var attempts = 0; attempts <= ServiceConnectionMonitor.MaximumAutoInstallAttempts; attempts++)
        {
            var retryAfter = attempts == 0 ? DateTime.MinValue : Now.Add(ServiceConnectionMonitor.AutoInstallCooldown(attempts));
            for (var second = 0; second <= 600; second++)
            {
                var now = Now.AddSeconds(second);
                var may = ServiceConnectionMonitor.MayAttemptAutoInstall(attempts, now, retryAfter);
                var waits = ServiceConnectionMonitor.ShouldWaitForAutoInstallRetry(attempts, now, retryAfter);

                Assert.IsFalse(may && waits, $"attempt {attempts} at +{second}s allowed a retry while waiting");
                if (attempts < ServiceConnectionMonitor.MaximumAutoInstallAttempts)
                    Assert.IsTrue(may || waits, $"attempt {attempts} at +{second}s gave up before the budget was spent");
            }
        }
    }

    [TestMethod]
    [DataRow(1, 60)]
    [DataRow(2, 120)]
    [DataRow(3, 240)]
    [DataRow(9, 480)]
    public void AutoInstallCooldown_GrowsThenCaps(int attempts, int expectedSeconds) =>
        Assert.AreEqual(TimeSpan.FromSeconds(expectedSeconds), ServiceConnectionMonitor.AutoInstallCooldown(attempts));
}
