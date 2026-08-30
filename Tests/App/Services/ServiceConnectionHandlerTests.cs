using System.Reflection;
using Lertaro.Core.Services.Search;
using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

// BeginServiceReconnectGracePeriod/ShouldWaitForServiceReconnect/ClearServiceReconnectState/
// ResetAutoInstallFlag all read/write PROCESS-WIDE static fields shared by every ServiceConnectionHandler
// instance (by design -- one reconnect timer serves every open window) -- so every test here must run
// un-parallelized and explicitly reset that state before AND after, or leftover state from one test (or
// a prior failed run) silently changes another test's outcome.
[TestClass]
[DoNotParallelize]
public sealed class ServiceConnectionHandlerTests
{
    private static ServiceConnectionHandler MakeHandler() => new(
        new SearchService(),
        onStatusUpdated: _ => { },
        onServiceInstallStarted: () => { },
        onServiceInstallCompleted: () => { },
        onServiceInstallError: _ => { },
        onServiceFailedToStart: () => { },
        onServiceReachable: () => { });

    private static void SetGlobalField(string name, object value) =>
        typeof(ServiceConnectionMonitor).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, value);

    [TestInitialize]
    [TestCleanup]
    public void ResetGlobalReconnectState()
    {
        MakeHandler().ClearServiceReconnectState();
        SetGlobalField("_globalAutoInstallingService", false);
    }

    [TestMethod]
    public void ShouldWaitForServiceReconnect_DefaultState_ReturnsFalse() =>
        Assert.IsFalse(MakeHandler().ShouldWaitForServiceReconnect());

    [TestMethod]
    public void BeginServiceReconnectGracePeriod_ThenShouldWait_ReturnsTrue()
    {
        var handler = MakeHandler();

        handler.BeginServiceReconnectGracePeriod();

        Assert.IsTrue(handler.ShouldWaitForServiceReconnect());
    }

    [TestMethod]
    public void ClearServiceReconnectState_AfterGracePeriodStarted_ShouldWaitReturnsFalseAgain()
    {
        var handler = MakeHandler();
        handler.BeginServiceReconnectGracePeriod();

        handler.ClearServiceReconnectState();

        Assert.IsFalse(handler.ShouldWaitForServiceReconnect());
    }

    [TestMethod]
    public void ShouldWaitForServiceReconnect_AutoInstallingRegardlessOfGracePeriod_ReturnsTrue()
    {
        SetGlobalField("_globalAutoInstallingService", true);

        Assert.IsTrue(MakeHandler().ShouldWaitForServiceReconnect());
    }

    [TestMethod]
    public void IsAutoInstallingService_ReflectsGlobalState()
    {
        SetGlobalField("_globalAutoInstallingService", true);

        Assert.IsTrue(MakeHandler().IsAutoInstallingService);
    }

    [TestMethod]
    public void ClearServiceReconnectState_ClearsAutoInstallingFlagToo()
    {
        SetGlobalField("_globalAutoInstallingService", true);
        var handler = MakeHandler();

        handler.ClearServiceReconnectState();

        Assert.IsFalse(handler.IsAutoInstallingService);
    }

    [TestMethod]
    public void HasAttemptedAutoInstall_ReflectsGlobalState()
    {
        SetGlobalField("_globalAutoInstallAttempted", true);

        Assert.IsTrue(MakeHandler().HasAttemptedAutoInstall);

        SetGlobalField("_globalAutoInstallAttempted", false);
    }

    [TestMethod]
    public void ResetAutoInstallFlag_ClearsHasAttemptedAutoInstall()
    {
        SetGlobalField("_globalAutoInstallAttempted", true);
        var handler = MakeHandler();

        handler.ResetAutoInstallFlag();

        Assert.IsFalse(handler.HasAttemptedAutoInstall);
    }

    [TestMethod]
    public void ResetAutoInstallFlag_DoesNotClearReconnectGracePeriod()
    {
        var handler = MakeHandler();
        handler.BeginServiceReconnectGracePeriod();

        handler.ResetAutoInstallFlag();

        Assert.IsTrue(handler.ShouldWaitForServiceReconnect());
    }

    [TestMethod]
    public void Constructor_NullSearchService_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new ServiceConnectionHandler(null!, _ => { }, () => { }, () => { }, _ => { }, () => { }, () => { }));

    [TestMethod]
    public void Constructor_NullOnStatusUpdated_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new ServiceConnectionHandler(new SearchService(), null!, () => { }, () => { }, _ => { }, () => { }, () => { }));

    [TestMethod]
    public void Constructor_NullOnServiceReachable_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new ServiceConnectionHandler(new SearchService(), _ => { }, () => { }, () => { }, _ => { }, () => { }, null!));
}
