using Lertaro.Cli.Search;

namespace Lertaro.Cli.Tests.Search;

[TestClass]
public sealed class AppSearchPipeClientTests
{
    [TestMethod]
    public void PipeNameFor_UsesTheSessionHash() => Assert.AreEqual(
        "Lertaro_App_Search_Pipe_session-hash",
        AppSearchPipeClient.PipeNameFor("session-hash"));
}
