using Lertaro.App.Services;
using Microsoft.Win32;

namespace Lertaro.App.Tests.Services;

// Which power events mean "make sure the hooks are up". A dropped low-level hook looks exactly like an
// installed one from inside the process that owns it, so this decision is the whole recovery -- and .NET's
// PowerModes has no member for the early resume a modern-standby machine sends, which is why the rule is
// "not suspend" rather than a list of resume values.
[TestClass]
public sealed class HookResumeWatcherTests
{
    private const int PbtApmResumeAutomatic = 0x12;

    [TestMethod]
    [DataRow(PowerModes.Resume, true)]
    [DataRow(PowerModes.StatusChange, true)]
    [DataRow(PowerModes.Suspend, false)]
    public void MeansRearm_RearmsOnEverythingExceptGoingToSleep(PowerModes mode, bool expected)
        => Assert.AreEqual(expected, HookResumeWatcher.MeansRearm(mode));

    [TestMethod]
    public void MeansRearm_RearmsOnAPowerEventNetDoesNotName()
        => Assert.IsTrue(HookResumeWatcher.MeansRearm((PowerModes)PbtApmResumeAutomatic));
}
