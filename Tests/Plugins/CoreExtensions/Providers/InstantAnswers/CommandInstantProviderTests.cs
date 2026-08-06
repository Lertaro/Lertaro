using Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.InstantAnswers;

[TestClass]
public sealed class CommandInstantProviderTests
{
    private static readonly CommandInstantProvider Provider = new();

    [TestMethod]
    public void GetInstantResults_EmptyQuery_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults(""));

    [TestMethod]
    public void GetInstantResults_HashPrefix_ReturnsRunAsAdminCommand()
    {
        var result = Provider.GetInstantResults("#dir").Single();

        Assert.AreEqual("runas:cmd.exe /k dir", result.ActionArgument);
        Assert.AreEqual("Execute", result.ActionType);
    }

    [TestMethod]
    public void GetInstantResults_DollarPrefix_ReturnsNormalCommand()
    {
        var result = Provider.GetInstantResults("$dir").Single();

        Assert.AreEqual("cmd.exe /k dir", result.ActionArgument);
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
