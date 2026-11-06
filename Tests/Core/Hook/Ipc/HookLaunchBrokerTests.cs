using Lertaro.Core.Hook.Ipc;

namespace Lertaro.Core.Tests.Hook.Ipc;

// The switch decides which levels the App is willing to ask the Service for, and the two answers are not
// interchangeable in cost: asking high first is what gets an administrator an elevated hook without a prompt,
// while not asking has to be exactly one request -- a second one would be an elevated request in disguise,
// which is what makes HookProcessBroker restart a hook that is already running.
[TestClass]
public sealed class HookLaunchBrokerTests
{
    [TestMethod]
    public void OffAsksForTheSessionsOwnLevelAlone()
        => CollectionAssert.AreEqual(new[] { false }, HookLaunchBroker.RequestPlan(false));

    [TestMethod]
    public void OnAsksHighFirstAndSettlesForTheSessionLevel()
        => CollectionAssert.AreEqual(new[] { true, false }, HookLaunchBroker.RequestPlan(true));
}
