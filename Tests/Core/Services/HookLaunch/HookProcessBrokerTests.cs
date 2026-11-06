using Lertaro.Core.Services.HookLaunch;

namespace Lertaro.Core.Tests.Services.HookLaunch;

// The dedup used to answer every request with whatever hook was already live, which is how a session that
// once got an unelevated hook kept it: the App asked for elevation, was handed the unelevated process as
// though that were the answer, and never asked again. These are the only three questions the replacement
// decision needs, and the probes behind them are expensive Win32 calls that must stay unasked when the
// answer is already known.
[TestClass]
public sealed class HookProcessBrokerTests
{
    [TestMethod]
    public void ARequestThatWantsNoElevationTakesTheLiveHook()
        => Assert.IsTrue(HookProcessBroker.KeepLiveHook(false, NeverAsked, NeverAsked));

    [TestMethod]
    public void AnElevatedRequestIsAnsweredByAnElevatedHook()
        => Assert.IsTrue(HookProcessBroker.KeepLiveHook(true, () => true, NeverAsked));

    [TestMethod]
    public void AnElevatedRequestReplacesALowHookWhenTheSessionCanDoBetter()
        => Assert.IsFalse(HookProcessBroker.KeepLiveHook(true, () => false, () => true));

    // #466's standard user: the live hook is all this session will ever get, so killing it to ask for
    // elevation would trade a working hook for nothing.
    [TestMethod]
    public void ALowHookSurvivesWhenTheSessionHasNoAdministrator()
        => Assert.IsTrue(HookProcessBroker.KeepLiveHook(true, () => false, () => false));

    [TestMethod]
    public void TheIntegrityProbeIsNotPaidForWhenTheRequestNeedsNoElevation()
    {
        var asked = 0;
        HookProcessBroker.KeepLiveHook(false, () => { asked++; return false; }, () => { asked++; return false; });
        Assert.AreEqual(0, asked);

        // And the session probe is not paid for once the live hook is known to be high.
        HookProcessBroker.KeepLiveHook(true, () => true, () => { asked++; return true; });
        Assert.AreEqual(0, asked);
    }

    private static bool NeverAsked() => throw new AssertFailedException("this probe must not be reached");
}
