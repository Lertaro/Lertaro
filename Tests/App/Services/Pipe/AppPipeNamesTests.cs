using Lertaro.App.Services.Pipe;

namespace Lertaro.App.Tests.Services.Pipe;

[TestClass]
public sealed class AppPipeNamesTests
{
    [TestMethod]
    public void Build_UsesTheCombinedSidAndSessionHash() => Assert.AreEqual(
        "Lertaro_App_Pipe_session-hash",
        AppPipeNames.Build("Lertaro_App_Pipe", "session-hash"));

    [TestMethod]
    public void Build_KeepsPipePurposesDistinct() => Assert.AreNotEqual(
        AppPipeNames.Build("Lertaro_App_Pipe", "session-hash"),
        AppPipeNames.Build("Lertaro_App_Search_Pipe", "session-hash"));
}
