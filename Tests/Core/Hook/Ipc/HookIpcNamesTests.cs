using Lertaro.Core.Hook.Ipc;

namespace Lertaro.Core.Tests.Hook.Ipc;

[TestClass]
public sealed class HookIpcNamesTests
{
    [TestMethod]
    public void EventPipeName_HasExpectedPrefixAndNoBackslashes()
    {
        var name = HookIpcNames.EventPipeName;

        Assert.IsTrue(name.StartsWith("Lertaro_Hook_Events_", StringComparison.Ordinal));
        Assert.DoesNotContain("\\", name);
    }

    [TestMethod]
    public void CmdPipeName_HasExpectedPrefixAndNoBackslashes()
    {
        var name = HookIpcNames.CmdPipeName;

        Assert.IsTrue(name.StartsWith("Lertaro_Hook_Cmds_", StringComparison.Ordinal));
        Assert.DoesNotContain("\\", name);
    }

    [TestMethod]
    public void EventPipeName_IsStableAcrossCalls()
    {
        // The name is built from the current user's session hash on every access, so this has to compare
        // two separate evaluations: reading one expression twice says nothing about the second call.
        var first = HookIpcNames.EventPipeName;
        var second = HookIpcNames.EventPipeName;

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void EventPipeName_AndCmdPipeName_AreDistinct() => Assert.AreNotEqual(HookIpcNames.EventPipeName, HookIpcNames.CmdPipeName);

    [TestMethod]
    public void BuildName_UsesTheCombinedSidAndSessionHashInsteadOfTheUserName() => Assert.AreEqual(
        "Lertaro_Hook_Events_session-hash",
        HookIpcNames.BuildName("Events", "session-hash"));
}
