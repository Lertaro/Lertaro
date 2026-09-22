using Lertaro.Core.Hook.Ipc;

namespace Lertaro.Core.Tests.Hook.Ipc;

// When the App dies, nothing else tells the hook to stop: the only other signal is a Stop message the dead
// App never sends, and the App's own kill of the hook process died with it. So the whole decision is
// "is the App I was launched for gone, and am I still the hook it registered with".
[TestClass]
public sealed class HookProcessLivenessTests
{
    [DataTestMethod]
    // the App this hook serves exited
    [DataRow(1234u, 1234u, true, true)]
    // still running, nothing to do
    [DataRow(1234u, 1234u, false, false)]
    // a newer App registered, so the old watch must not stop it
    [DataRow(1234u, 5678u, true, false)]
    // no App ever registered: a hook awaiting one stays put
    [DataRow(0u, 0u, true, false)]
    public void ShouldSelfStop_CoversEachCase(uint watchedPid, uint currentAppPid, bool appIsGone, bool expected) =>
        Assert.AreEqual(expected, HookProcess.ShouldSelfStop(watchedPid, currentAppPid, appIsGone));
}
