using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

// Whether the process answering LertaroPipe is the service is the whole security decision behind
// ServicePipe; asking the kernel and the SCM for the two PIDs is real Win32 and stays untested on purpose.
[TestClass]
public sealed class ServicePipeTests
{
    [TestMethod]
    [DataRow(1234, 1234u, false, true, "the service itself")]
    [DataRow(1234, 1234u, true, true, "the service itself, debug build")]
    [DataRow(666, 1234u, false, false, "another process while the service runs")]
    [DataRow(666, 1234u, true, false, "a debug build still checks a running service")]
    [DataRow(null, 1234u, false, false, "the kernel could not say who serves the pipe")]
    [DataRow(666, 0u, false, false, "the service is stopped and someone else took the name")]
    [DataRow(null, 0u, false, false, "nothing is known at all")]
    [DataRow(666, 0u, true, true, "a debug build talking to the console-mode service")]
    public void IsTrustedServer(int? serverPid, uint servicePid, bool allowUnregisteredServer, bool expected, string because) =>
        Assert.AreEqual(expected, ServicePipe.IsTrustedServer(serverPid, servicePid, allowUnregisteredServer), because);
}
