using Lertaro.App.Services.AppLifecycle;

namespace Lertaro.App.Tests.Services.AppLifecycle;

[TestClass]
public sealed class AppRestartServiceTests
{
    [TestMethod]
    public void TryGetParentProcessId_ReadsRestartArgument()
    {
        var found = AppRestartService.TryGetParentProcessId(["--lertaro-restart-wait-pid=1234"], out var processId);

        Assert.IsTrue(found);
        Assert.AreEqual(1234, processId);
    }

    [TestMethod]
    public void TryGetParentProcessId_RejectsInvalidArguments()
    {
        var found = AppRestartService.TryGetParentProcessId(["--lertaro-restart-wait-pid=0"], out var processId);

        Assert.IsFalse(found);
        Assert.AreEqual(0, processId);
    }

    [TestMethod]
    public void TryGetParentProcessId_IgnoresUnrelatedArguments()
    {
        var found = AppRestartService.TryGetParentProcessId(["lertaro://settings/about"], out _);

        Assert.IsFalse(found);
    }
}
