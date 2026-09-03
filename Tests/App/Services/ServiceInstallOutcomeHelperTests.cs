using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

[TestClass]
public sealed class ServiceInstallOutcomeHelperTests
{
    [TestMethod]
    public void DetermineResult_RequiresSuccessfulInstallRegistrationAndStart()
    {
        Assert.AreEqual(
            ServiceInstallManager.SilentInstallResult.Started,
            ServiceInstallOutcomeHelper.DetermineResult(true, true, true));
        Assert.AreEqual(
            ServiceInstallManager.SilentInstallResult.Failed,
            ServiceInstallOutcomeHelper.DetermineResult(true, true, false));
        Assert.AreEqual(
            ServiceInstallManager.SilentInstallResult.Failed,
            ServiceInstallOutcomeHelper.DetermineResult(true, false, false));
    }
}
