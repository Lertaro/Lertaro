using Lertaro.Core.Hook.Ipc;

namespace Lertaro.Core.Tests.Hook.Ipc;

// Which peer identity counts as an impostor is the whole security decision behind verifying the hook
// pipe; asking the OS for the owning PID is real Win32 and stays untested on purpose.
[TestClass]
public sealed class HookPipePeerTests
{
    [TestMethod]
    [DataRow(null, 42, false)]
    [DataRow(42, 42, false)]
    [DataRow(7, 42, true)]
    [DataRow(7, 0, false)]
    public void IsImpersonation_RejectsOnlyAKnownDifferentOwner(int? serverPid, int launchedHookPid, bool expected) =>
        Assert.AreEqual(expected, HookPipePeer.IsImpersonation(serverPid, launchedHookPid));
}
