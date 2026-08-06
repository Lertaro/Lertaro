using Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.InstantAnswers;

// Environment.SetEnvironmentVariable(..., EnvironmentVariableTarget.Process) only affects this test
// process's own environment block, never the real user/machine environment -- safe to set and clear
// without touching anything outside the run.
//
// Safe for the machine is not the same as safe for the other tests, though: that environment block is
// shared by every test in the process, and TestCleanup below clears the variable after EVERY method in
// this class, not just the ones that set it. MSTest parallelizes at the method level here (see
// MSTestSettings), so a sibling test finishing would delete the variable out from under the test that
// had just set it, which failed intermittently and only when the machine was loaded enough to overlap
// them. [DoNotParallelize] for the same reason nineteen other classes in this suite have it.
[TestClass]
[DoNotParallelize]
public sealed class EnvironmentVariableInstantProviderTests
{
    private const string TestVarName = "LERTAROTESTVARXYZ";

    [TestCleanup]
    public void CleanupTestVar() => Environment.SetEnvironmentVariable(TestVarName, null, EnvironmentVariableTarget.Process);

    [TestMethod]
    public void GetInstantResults_EmptyQuery_ReturnsNothing() =>
        Assert.IsEmpty(new EnvironmentVariableInstantProvider().GetInstantResults(""));

    [TestMethod]
    public void GetInstantResults_KnownFullVariableSyntax_ExpandsToRealValue()
    {
        var result = new EnvironmentVariableInstantProvider().GetInstantResults("%TEMP%").Single();

        Assert.AreEqual(Environment.ExpandEnvironmentVariables("%TEMP%"), result.Title);
    }

    [TestMethod]
    public void GetInstantResults_KnownVariableWithExistingDirectory_OffersExecuteAction()
    {
        // TEMP always points at a real, existing directory on any Windows machine.
        var result = new EnvironmentVariableInstantProvider().GetInstantResults("%TEMP%").Single();

        Assert.AreEqual("Execute", result.ActionType);
    }

    [TestMethod]
    public void GetInstantResults_UnknownVariableFullSyntax_FuzzySearchFindsNothing()
    {
        var results = new EnvironmentVariableInstantProvider().GetInstantResults("%NoSuchLertaroVarXyz123%").ToList();

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void GetInstantResults_PartialNameWithLeadingPercent_FuzzyModeFindsPrefixMatch()
    {
        Environment.SetEnvironmentVariable(TestVarName, "hello-value", EnvironmentVariableTarget.Process);

        var results = new EnvironmentVariableInstantProvider().GetInstantResults("%LERTAROTESTVARX").ToList();

        Assert.IsTrue(results.Any(r => r.Title == $"%{TestVarName}%"));
    }

    [TestMethod]
    public void GetInstantResults_BarePercent_FuzzyModeListsSomeVariables()
    {
        var results = new EnvironmentVariableInstantProvider().GetInstantResults("%").ToList();

        Assert.IsNotEmpty(results);
        Assert.IsTrue(results.All(r => r.Title.StartsWith('%') && r.Title.EndsWith('%')));
    }

    [TestMethod]
    public void GetInstantResults_NoPercentSign_ReturnsNothing() =>
        Assert.IsEmpty(new EnvironmentVariableInstantProvider().GetInstantResults("just text"));

    [TestMethod]
    public void GetHighlightMask_EmptyQuery_ReturnsNull() =>
        Assert.IsNull(new EnvironmentVariableInstantProvider().GetHighlightMask("%TEMP%", ""));

    [TestMethod]
    public void GetHighlightMask_NonPercentText_ReturnsAllFalseMask()
    {
        var mask = new EnvironmentVariableInstantProvider().GetHighlightMask("plain text", "%TE");

        Assert.IsNotNull(mask);
        Assert.IsTrue(mask.All(b => !b));
    }
}
