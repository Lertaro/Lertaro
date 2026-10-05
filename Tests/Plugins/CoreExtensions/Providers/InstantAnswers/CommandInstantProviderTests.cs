using Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.InstantAnswers;

[TestClass]
public sealed class CommandInstantProviderTests
{
    private static readonly CommandInstantProvider Provider = new();

    [TestMethod]
    public void GetInstantResults_EmptyQuery_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults(""));

    [TestMethod]
    [DataRow("#dir", true)]
    [DataRow("$dir", false)]
    public void GetInstantResults_PrefixControlsElevationWhenExecuted(string query, bool expectedAdmin)
    {
        (string Command, bool Admin)? executed = null;
        var provider = new CommandInstantProvider((command, _, admin, _, _) => executed = (command, admin));
        var result = Assert.ContainsSingle(provider.GetInstantResults(query));
        Assert.AreEqual("Execute", result.ActionType);
        Assert.IsNull(executed);
        Assert.IsNotNull(result.OnExecute);
        result.OnExecute();
        Assert.AreEqual(("dir", expectedAdmin), executed);
    }

    [TestMethod]
    public void GetInstantResults_NoRecognizedPrefix_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("dir"));

    [TestMethod]
    public void GetInstantResults_PrefixWithNoTarget_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("#"));

    [TestMethod]
    public void GetInstantResults_PrefixWithOnlyWhitespaceTarget_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("#   "));

    [TestMethod]
    public void GetHighlightMask_EmptyQuery_ReturnsNull() => Assert.IsNull(Provider.GetHighlightMask("text", ""));

    [TestMethod]
    public void GetHighlightMask_EmptyTarget_ReturnsAllFalseMask()
    {
        var mask = Provider.GetHighlightMask("text", "#");

        Assert.IsNotNull(mask);
        Assert.IsTrue(mask.All(b => !b));
    }
}
